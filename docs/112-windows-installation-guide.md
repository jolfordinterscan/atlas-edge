# Atlas Edge Windows installation guide

This guide applies to the unsigned installer foundation. Production use requires
completed signing, legal, security, and Windows verification gates.

## Build

```powershell
pwsh .\installer\scripts\build-installer.ps1 -Configuration Release -BuildNumber local
```

Verify `manifest.json`, SHA-256 sidecars, and Authenticode signatures before use.

## Interactive installation

```cmd
msiexec /i AtlasEdge-0.6.0-win-x64.msi /l*v AtlasEdge-install.log
```

Review and accept the EULA, acknowledge telemetry/privacy, confirm organizational
authorization, review the directory/readiness screen, and install. UAC elevation
is still required.

## Silent installation and rejection test

```cmd
msiexec /i AtlasEdge-0.6.0-win-x64.msi /qn ATLAS_ACCEPT_EULA=1 ATLAS_ACCEPT_TELEMETRY=1 ATLAS_ADMIN_AUTHORIZED=1 /l*v AtlasEdge-install.log
```

This command must fail before installation because acceptance is absent:

```cmd
msiexec /i AtlasEdge-0.6.0-win-x64.msi /qn /l*v AtlasEdge-rejected.log
```

## Service and Event Log verification

```powershell
Get-Service -Name 'Atlas Edge Runtime'
sc.exe qc "Atlas Edge Runtime"
sc.exe qfailure "Atlas Edge Runtime"
Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='Atlas Edge Runtime'} -MaxEvents 20
Get-ItemProperty 'HKLM:\Software\InterScan\Atlas Edge\InstallerAcceptance'
```

Confirm events 1500/1501 on start and 1502/1503 on graceful stop. Verify the
service image path is quoted, startup is Automatic, recovery is configured, and
the acceptance record contains only approved fields.

## Repair, upgrade, and downgrade

```cmd
msiexec /fa AtlasEdge-0.6.0-win-x64.msi /qn /l*v AtlasEdge-repair.log
msiexec /i AtlasEdge-newer-win-x64.msi /l*v AtlasEdge-upgrade.log
msiexec /i AtlasEdge-older-win-x64.msi /l*v AtlasEdge-downgrade.log
```

Repair restores binaries without overwriting retained ProgramData. Upgrade must
preserve data and one service registration. The older package must be blocked by
`MajorUpgrade`.

## Uninstall and retained data

```cmd
msiexec /x {PRODUCT-CODE} /qn /l*v AtlasEdge-uninstall.log
```

The runtime, service, and Event Log source are removed. Configuration, identity,
diagnostics, and installer acceptance metadata under
`C:\ProgramData\InterScan\Atlas Edge` and/or the documented HKLM record are
retained. Remove retained data only after explicit customer authorization:

```powershell
Remove-Item -LiteralPath 'C:\ProgramData\InterScan\Atlas Edge' -Recurse -Force
Remove-Item -LiteralPath 'HKLM:\Software\InterScan\Atlas Edge' -Recurse -Force
```

Resolve exact targets first and preserve anything required for support or audit.

## Troubleshooting and exit codes

Preserve verbose logs and record package hash, Windows build, free space, service
state, Event IDs, and sanitized errors. Do not collect secrets or document
content. Common codes: `0` success, `3010` success/reboot required, `1602`
cancelled, `1603` fatal error, `1618` another installation active, `1638`
another version installed. Follow [Windows verification](../installer/WINDOWS-VERIFICATION.md)
and escalate through approved support contacts.
