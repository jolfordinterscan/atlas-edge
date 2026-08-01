# Atlas Edge installer release-security checklist

No production package may be released until every applicable gate is checked,
evidence is attached to the release record, and authorized approval is recorded.

## Legal and brand gates

- [ ] Legal-approved EULA inserted; no `DRAFT-*` EULA is configured.
- [ ] Legal-approved telemetry/privacy disclosure inserted; no `DRAFT-*` disclosure is configured.
- [ ] Official InterScan logo checksum verified.
- [ ] Official icon present or documented exception approved.

## Build and test gates

- [ ] Runtime built in Release configuration.
- [ ] Build reports 0 warnings.
- [ ] Full repository tests pass with 0 failures.
- [ ] Installer tests pass with 0 failures.
- [ ] Windows VM checklist passes and evidence is retained.
- [ ] SHA-256 manifest generated and verified.
- [ ] Release artifacts malware-scanned using the approved enterprise process.

## Security and privacy gates

- [ ] No development mocks are enabled.
- [ ] No real secrets are embedded.
- [ ] No enrollment code is embedded.
- [ ] No production token is embedded.
- [ ] No inbound firewall rule is created.
- [ ] No HTTP listener is installed or started.
- [ ] Program Files ACL verified.
- [ ] ProgramData ACL verified.
- [ ] Service identity reviewed; LocalSystem pilot exception explicitly approved or remediated.
- [ ] Service recovery verified.
- [ ] Event Log registration and redaction verified.
- [ ] Telemetry categories and excluded content match approved disclosure and customer policy.

## Installer behavior gates

- [ ] Upgrade tested.
- [ ] Downgrade blocked.
- [ ] Repair tested.
- [ ] Uninstall tested.
- [ ] Retention policy verified.
- [ ] Silent install tested.
- [ ] EULA acceptance enforced interactively and silently.
- [ ] Telemetry acknowledgement enforced interactively and silently.
- [ ] Administrator authorization enforced interactively and silently.
- [ ] Acceptance record contains only approved fields and UTC behavior is verified.
- [ ] No automatic updater is installed or implied as enabled.

## Signing and release gates

- [ ] Runtime executable signed.
- [ ] MSI signed.
- [ ] Bootstrapper signed.
- [ ] All Authenticode signatures and timestamp chains verified.
- [ ] Signing inputs came from the approved external secret store.
- [ ] Release approval recorded by authorized engineering, security, product, and legal reviewers.
