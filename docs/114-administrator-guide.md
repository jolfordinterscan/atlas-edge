# Atlas Edge administrator guide

## Service and paths

- Service name/display name: `Atlas Edge Runtime`
- Runtime: `C:\Program Files\InterScan\Atlas Edge`
- Data: `C:\ProgramData\InterScan\Atlas Edge`
- Configuration placeholder: `C:\ProgramData\InterScan\Atlas Edge\configuration\appsettings.json`
- Identity: `C:\ProgramData\InterScan\Atlas Edge\identity`
- Diagnostics: `C:\ProgramData\InterScan\Atlas Edge\diagnostics`
- Acceptance metadata: `HKLM\Software\InterScan\Atlas Edge\InstallerAcceptance`
- Event Viewer: Windows Logs → Application → source `Atlas Edge Runtime`

The Checkpoint 15A local configuration model remains a placeholder; do not assume
that editing the ProgramData file dynamically reconfigures the running service.

## Service operation and health

```powershell
Get-Service -Name 'Atlas Edge Runtime'
Start-Service -Name 'Atlas Edge Runtime'
Stop-Service -Name 'Atlas Edge Runtime'
Restart-Service -Name 'Atlas Edge Runtime'
sc.exe qc "Atlas Edge Runtime"
sc.exe qfailure "Atlas Edge Runtime"
Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='Atlas Edge Runtime'} -MaxEvents 50
```

Healthy startup emits 1500 then 1501. Graceful stop emits 1502 then 1503. Check
uptime, last service heartbeat, last discovery, and last health update through
approved diagnostics when available. Unknown scanner metrics must remain Unknown.

## Safe log collection and troubleshooting

Collect package hash/version, Windows build, service state, relevant 1500–1503
events, sanitized stable error codes, and precise reproduction steps. Do not
collect raw serials/hardware IDs, tokens, enrollment codes, response bodies,
document content, unrestricted logs, or unrelated user files. Never disable TLS,
add inbound firewall rules, change service executable paths, grant Users write
access, load arbitrary DLLs, or enable mock providers on a pilot machine.

Use verbose MSI logs for installation failures. Repair with:

```cmd
msiexec /fa AtlasEdge-0.6.0-win-x64.msi /qn /l*v AtlasEdge-repair.log
```

Uninstall with:

```cmd
msiexec /x {PRODUCT-CODE} /qn /l*v AtlasEdge-uninstall.log
```

Uninstall retains customer configuration, identity, diagnostics, and acceptance
metadata. Clean retained data only with explicit customer authorization and
after support/retention needs are resolved; use the exact commands in the
[Windows installation guide](112-windows-installation-guide.md).

## Support handoff

- Customer organization/site: **[CUSTOMER TO PROVIDE]**
- Workstation asset ID (not username): **[CUSTOMER TO PROVIDE]**
- Package version/build/SHA-256: **[ADMINISTRATOR TO PROVIDE]**
- Sanitized incident time and error codes: **[ADMINISTRATOR TO PROVIDE]**
- Customer escalation owner: **[CUSTOMER TO PROVIDE]**
- InterScan support destination: **[INTERSCAN TO PROVIDE]**
- Approved diagnostic bundle location: **[CUSTOMER TO PROVIDE]**
