[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$installerRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $installerRoot 'assets/interscan-logo.svg'
$expectedHash = '981c99b7d7b4a4985764bbca42b03998ef906fcac1444e0cbc45b7ba52cb7d0d'

if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
    throw "Official InterScan logo is missing: $source"
}

$actualHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne $expectedHash) {
    throw "Official InterScan logo checksum mismatch. Expected $expectedHash; got $actualHash."
}

$magick = Get-Command magick -ErrorAction SilentlyContinue
if ($null -eq $magick) {
    throw 'ImageMagick is required to create WiX branding assets. Install it and ensure magick.exe is on PATH.'
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$logoPng = Join-Path $OutputDirectory 'interscan-logo.png'
$bannerBmp = Join-Path $OutputDirectory 'interscan-banner.bmp'
$dialogBmp = Join-Path $OutputDirectory 'interscan-dialog.bmp'

& $magick.Source -background none $source -resize '450x150' $logoPng
& $magick.Source -size '493x58' 'xc:white' '(' $source -resize '180x50' ')' -gravity west -geometry '+16+0' -composite "BMP3:$bannerBmp"
& $magick.Source -size '493x312' 'xc:white' '(' $source -resize '300x100' ')' -gravity north -geometry '+0+24' -composite "BMP3:$dialogBmp"

foreach ($required in @($logoPng, $bannerBmp, $dialogBmp)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Brand asset generation failed: $required"
    }
}
