# Atlas Edge pilot deployment guide

> Pilot planning document. Legal and privacy terms remain subject to approved customer agreements and InterScan legal review.

## Pilot objectives and scope

The pilot validates safe installation, Windows Service operation, read-only
scanner discovery and health observation, operational evidence boundaries, and
support procedures. Scope is US-first, customer-authorized Windows 10/11 x64
workstations. It does not establish broader geographic, regulatory, or
production support commitments.

No AI, prediction, recommendation, scanner control, remote control, production
Atlas connectivity, or automatic update capability is included.

## Roles

- Customer sponsor: approves objectives and success criteria.
- Customer IT/security: approves software, monitoring scope, network policy,
  service identity, AV/EDR treatment, and removal.
- Customer workstation administrator: inventories targets and executes or
  deploys the installer.
- InterScan pilot lead: coordinates package, verification, and escalation.
- InterScan engineering/security: reviews diagnostics and defects within the
  approved data boundary.
- InterScan legal/privacy reviewers: approve final customer-facing terms.

## Prerequisites

- Supported x64 Windows host with current security updates.
- Local administrator or approved enterprise deployment system.
- Release-approved, signed package and verified SHA-256 manifest.
- Approved EULA, telemetry/privacy disclosure, and customer monitoring policy.
- Inventory of workstations, scanners, drivers, sites, owners, and maintenance windows.
- Backup/rollback procedure and support contacts.
- Windows protected credential storage gap resolved before production enrollment.

## Inventory worksheet

| Field | Customer entry |
|---|---|
| Pilot site / business unit | |
| Workstation asset ID | |
| Windows edition/build/x64 | |
| Scanner manufacturer/model | |
| Approved masked scanner ID | |
| Connection/protocol | |
| Driver/provider/version | |
| Local administrator owner | |
| Maintenance window | |
| AV/EDR exception owner | |
| Proxy/firewall owner | |
| Rollback approval | |

Do not put passwords, tokens, enrollment codes, raw document data, or private
keys in the worksheet.

## AV/EDR and network review

Allow the signed runtime and installer only after security review. Do not broadly
exclude Program Files or ProgramData. Atlas Edge remains outbound-only and adds
no listener or inbound firewall rule. Current installer configuration uses null
transport and non-routable `.invalid` HTTPS placeholders. Any future enrollment
or egress requires separately approved HTTPS destinations, proxy behavior, TLS
inspection policy, certificate trust, and tenant binding.

## Deployment sequence

1. Approve pilot scope, disclosure, service identity, hosts, and maintenance window.
2. Verify signatures, hashes, release checklist, and Windows VM evidence.
3. Capture baseline services, listening ports, scanner behavior, and free space.
4. Run interactive or accepted silent installation from a protected package source.
5. Verify service, Event Log, ACLs, recovery, no inbound listener, and scanner behavior.
6. Observe the agreed pilot period and collect only authorized diagnostics.
7. Review outcomes against success criteria and decide expand, remediate, or roll back.

## Enrollment sequence

Enrollment is currently implemented only against a local HTTPS mock. Production
Atlas enrollment is not part of this installer checkpoint. A future approved
sequence would issue a one-time tenant-scoped code, establish revocable device
identity, validate returned HTTPS endpoints, persist credentials in a completed
Windows protected store, and verify sanitized logs. Do not configure production
enrollment during this pilot foundation unless a later approved checkpoint
explicitly authorizes it.

## Verification and success criteria

Use [the Windows checklist](../installer/WINDOWS-VERIFICATION.md). Success means
installation/repair/uninstall are repeatable, the service survives reboot and
recovers as documented, existing scanner behavior is unchanged, no inbound port
appears, operational observations remain accurate or Unknown, disclosures match
actual behavior, and support can diagnose failures without prohibited content.

## Rollback and escalation

Stop the service, collect only approved logs, uninstall with verbose logging,
confirm service/binaries are removed, and retain ProgramData pending customer
decision. Do not silently delete identity or diagnostics. Escalation records
should contain package/hash, timestamps, Windows build, sanitized error codes,
reproduction steps, and approved contact placeholders:

- Customer incident owner: **[CUSTOMER TO PROVIDE]**
- InterScan pilot lead: **[INTERSCAN TO PROVIDE]**
- Security escalation: **[INTERSCAN TO PROVIDE]**

## Current limitations

No Windows installation has been validated from the macOS development host.
Windows credential protection, production enrollment/transport, code signing,
legal approval, automatic updates, durable production queue validation, and
vendor-specific metric coverage remain release gates or future work.
