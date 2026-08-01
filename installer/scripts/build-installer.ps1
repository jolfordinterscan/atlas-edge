[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Version,
    [string]$BuildNumber = 'local',
    [switch]$Sign
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$installerRoot = Join-Path $repositoryRoot 'installer'
$artifactRoot = Join-Path $repositoryRoot 'artifacts/installer'
$stagingRoot = Join-Path $artifactRoot 'staging'
$publishRoot = Join-Path $stagingRoot 'runtime'
$brandRoot = Join-Path $stagingRoot 'branding'

if ([string]::IsNullOrWhiteSpace($Version)) {
    $versionText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'VERSION.md') -Raw
    $match = [regex]::Match($versionText, '(?m)^- Repository version:\s*([0-9]+\.[0-9]+\.[0-9]+)')
    if (-not $match.Success) {
        throw 'VERSION.md does not contain an MSI-compatible Current version.'
    }
    $Version = $match.Groups[1].Value
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Installer version '$Version' must contain exactly three numeric fields."
}

New-Item -ItemType Directory -Force -Path $publishRoot, $brandRoot | Out-Null

dotnet publish (Join-Path $repositoryRoot 'src/Atlas.Edge.Runtime/Atlas.Edge.Runtime.csproj') `
    -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false `
    -o $publishRoot
if ($LASTEXITCODE -ne 0) { throw 'Runtime publish failed.' }

Copy-Item -LiteralPath (Join-Path $installerRoot 'config/appsettings.json') `
    -Destination (Join-Path $publishRoot 'appsettings.json') -Force

& (Join-Path $PSScriptRoot 'prepare-brand-assets.ps1') -OutputDirectory $brandRoot

if ($Sign) {
    if ([string]::IsNullOrWhiteSpace($env:ATLAS_EDGE_SIGN_CERTIFICATE_PATH) -or
        [string]::IsNullOrWhiteSpace($env:ATLAS_EDGE_SIGN_CERTIFICATE_PASSWORD) -or
        [string]::IsNullOrWhiteSpace($env:ATLAS_EDGE_SIGN_TIMESTAMP_URL)) {
        throw 'Signing requires ATLAS_EDGE_SIGN_CERTIFICATE_PATH, ATLAS_EDGE_SIGN_CERTIFICATE_PASSWORD, and ATLAS_EDGE_SIGN_TIMESTAMP_URL.'
    }
    & signtool sign /fd SHA256 /f $env:ATLAS_EDGE_SIGN_CERTIFICATE_PATH `
        /p $env:ATLAS_EDGE_SIGN_CERTIFICATE_PASSWORD /tr $env:ATLAS_EDGE_SIGN_TIMESTAMP_URL `
        /td SHA256 (Join-Path $publishRoot 'Atlas.Edge.Runtime.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Runtime signing failed.' }
}

$payloadFile = Join-Path $stagingRoot 'RuntimePayload.wxs'
$files = Get-ChildItem -LiteralPath $publishRoot -File | Where-Object {
    $_.Name -ne 'Atlas.Edge.Runtime.exe'
} | Sort-Object Name
$xml = [System.Text.StringBuilder]::new()
[void]$xml.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
[void]$xml.AppendLine('  <Fragment>')
[void]$xml.AppendLine('    <ComponentGroup Id="RuntimePayload" Directory="INSTALLFOLDER">')
$index = 0
foreach ($file in $files) {
    $index++
    $escapedPath = [Security.SecurityElement]::Escape($file.FullName)
    [void]$xml.AppendLine("      <Component Id=`"RuntimePayload$index`" Guid=`"*`"><File Source=`"$escapedPath`" /></Component>")
}
[void]$xml.AppendLine('    </ComponentGroup>')
[void]$xml.AppendLine('  </Fragment>')
[void]$xml.AppendLine('</Wix>')
[IO.File]::WriteAllText($payloadFile, $xml.ToString(), [Text.UTF8Encoding]::new($false))

$msiOutput = Join-Path $stagingRoot 'AtlasEdge.msi'
dotnet build (Join-Path $installerRoot 'Atlas.Edge.Installer/Atlas.Edge.Installer.wixproj') `
    -c $Configuration `
    -p:ProductVersion=$Version `
    -p:RuntimePayloadFile=$payloadFile `
    -p:RuntimePublishDir=$publishRoot `
    -p:InstallerRoot=$installerRoot `
    -p:BrandAssetsDir=$brandRoot `
    -p:OutputPath=$stagingRoot
if ($LASTEXITCODE -ne 0) { throw 'MSI build failed.' }

$builtMsi = Get-ChildItem -LiteralPath $stagingRoot -Filter '*.msi' -Recurse | Select-Object -First 1
if ($null -eq $builtMsi) { throw 'WiX completed without producing an MSI.' }
Copy-Item -LiteralPath $builtMsi.FullName -Destination $msiOutput -Force

if ($Sign) {
    & signtool sign /fd SHA256 /f $env:ATLAS_EDGE_SIGN_CERTIFICATE_PATH `
        /p $env:ATLAS_EDGE_SIGN_CERTIFICATE_PASSWORD /tr $env:ATLAS_EDGE_SIGN_TIMESTAMP_URL `
        /td SHA256 $msiOutput
    if ($LASTEXITCODE -ne 0) { throw 'MSI signing failed.' }
}

dotnet build (Join-Path $installerRoot 'Atlas.Edge.Bootstrapper/Atlas.Edge.Bootstrapper.wixproj') `
    -c $Configuration `
    -p:ProductVersion=$Version `
    -p:BrandAssetsDir=$brandRoot `
    -p:MsiPath=$msiOutput `
    -p:OutputPath=$stagingRoot
if ($LASTEXITCODE -ne 0) { throw 'Bootstrapper build failed.' }

$setup = Get-ChildItem -LiteralPath $stagingRoot -Filter '*.exe' -Recurse |
    Where-Object { $_.Name -ne 'Atlas.Edge.Runtime.exe' } | Select-Object -First 1
if ($null -eq $setup) { throw 'WiX completed without producing a bootstrapper executable.' }

if ($Sign) {
    & signtool sign /fd SHA256 /f $env:ATLAS_EDGE_SIGN_CERTIFICATE_PATH `
        /p $env:ATLAS_EDGE_SIGN_CERTIFICATE_PASSWORD /tr $env:ATLAS_EDGE_SIGN_TIMESTAMP_URL `
        /td SHA256 $setup.FullName
    if ($LASTEXITCODE -ne 0) { throw 'Bootstrapper signing failed.' }
}

$releaseRoot = Join-Path $artifactRoot $Version
New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null
$outputs = @(
    @{ Source = $msiOutput; Name = "AtlasEdge-$Version-win-x64.msi" },
    @{ Source = $setup.FullName; Name = "AtlasEdgeSetup-$Version-win-x64.exe" }
)
$manifestFiles = @()
foreach ($output in $outputs) {
    $destination = Join-Path $releaseRoot $output.Name
    Copy-Item -LiteralPath $output.Source -Destination $destination -Force
    $hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($output.Name)" | Set-Content -LiteralPath "$destination.sha256" -Encoding ascii
    $manifestFiles += [ordered]@{
        fileName = $output.Name
        architecture = 'win-x64'
        sha256 = $hash
        signingStatus = $(if ($Sign) { 'signed' } else { 'unsigned' })
    }
}

$manifest = [ordered]@{
    product = 'Atlas Edge'
    publisher = 'InterScan'
    version = $Version
    buildNumber = $BuildNumber
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    files = $manifestFiles
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $releaseRoot 'manifest.json') -Encoding utf8

Write-Host "Installer artifacts: $releaseRoot"
