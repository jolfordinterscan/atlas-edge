# Windows verification checklist

Record Windows version, architecture, VM/hardware identity, package version,
build number, hashes, signature state, and tester for every run.

- [ ] Clean interactive install on supported Windows 10 and Windows 11 x64.
- [ ] Non-administrator install fails before changing the machine.
- [ ] Runtime is under `C:\Program Files\InterScan\Atlas Edge` and SCM image path is quoted.
- [ ] Shared data is under `C:\ProgramData\InterScan\Atlas Edge` with only SYSTEM/Administrators write access.
- [ ] `Atlas Edge Runtime` appears in Services with the specified display name and description.
- [ ] Startup type is Automatic and the service starts after install.
- [ ] Application Event Log contains lifecycle events 1500 and 1501.
- [ ] Reboot preserves configuration/identity and starts the service.
- [ ] Graceful stop emits lifecycle events 1502 and 1503.
- [ ] Forced termination exercises restart recovery for first, second, and subsequent failures.
- [ ] Repair restores missing binaries without overwriting ProgramData configuration.
- [ ] Upgrade preserves ProgramData, identity, and diagnostics and leaves one service registration.
- [ ] Downgrade is blocked with the configured message.
- [ ] Uninstall removes binaries, service, and Event Log source but retains ProgramData.
- [ ] Reinstall succeeds and reuses retained local configuration safely.
- [ ] Silent install, repair, and uninstall commands return expected exit codes and verbose logs.
- [ ] Paths containing spaces work for service start, repair, upgrade, and uninstall.
- [ ] `netstat -abno` confirms Atlas Edge adds no inbound listening port.
- [ ] Windows Firewall contains no Atlas Edge inbound rule.
- [ ] Runtime logs contain no tokens, enrollment codes, raw serials, or hardware IDs.
- [ ] Scanner discovery behavior and counts match the same runtime run outside the installer.
- [ ] Event Log provider displays events without raw secrets or mock data.
- [ ] Authenticode signatures and SHA-256 sidecars verify for production-signed artifacts.
- [ ] Welcome screen branding, product, publisher, subtitle, and supporting copy render correctly.
- [ ] EULA screen renders readable draft text and blocks Next without acceptance.
- [ ] Telemetry/privacy screen renders and blocks Next without acknowledgement.
- [ ] Administrator authorization screen blocks Next without confirmation.
- [ ] Silent install fails with an actionable log when any required acceptance property is absent.
- [ ] Silent install succeeds when `ATLAS_ACCEPT_EULA=1`, `ATLAS_ACCEPT_TELEMETRY=1`, and `ATLAS_ADMIN_AUTHORIZED=1` are supplied.
- [ ] Installer version, both document versions, installation mode, and acceptance timestamp are recorded under `HKLM\Software\InterScan\Atlas Edge\InstallerAcceptance`.
- [ ] Acceptance timestamp is verified as UTC; do not approve the pilot if the Windows Installer session value is local time.
- [ ] Acceptance record contains no username, password, token, enrollment code, scanner identifier, or document content.
- [ ] Completion screen does not claim enrollment, cloud connectivity, scanner discovery, telemetry delivery, page counts, prediction, or AI.
- [ ] EULA and disclosure RTF text is readable at 100%, 125%, 150%, and 200% Windows scaling.
- [ ] All screens fit at 100%, 125%, 150%, and 200% scaling without clipped controls.
- [ ] Keyboard-only navigation reaches every required checkbox and button in a meaningful order.
- [ ] Screen-reader labels are meaningful where supported by Windows Installer controls.
- [ ] Back and Next navigation preserves acknowledgement state appropriately during the same session.
- [ ] Automatic-update disclosure is visible and no updater, scheduled task, or update service is installed.
