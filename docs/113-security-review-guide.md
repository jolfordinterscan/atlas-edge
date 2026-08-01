# Atlas Edge security review guide

## Architecture and trust boundaries

Atlas Edge is a per-machine Windows Service that observes approved local scanner
and workstation signals and is designed for authenticated outbound HTTPS. The
installer baseline uses null transport and non-routable placeholders. Trust
boundaries include Windows Installer/UAC, SCM service identity, Program Files,
ProgramData, scanner/driver interfaces, the credential-store abstraction, and
any future approved Atlas endpoint.

There is no inbound listener, firewall exception, remote-control path, scanner
command, image acquisition, arbitrary plug-in loader, or shell execution added
by the installer.

## Host controls

- Program Files inherits protected machine ACLs with no user write grant.
- ProgramData explicitly grants full control only to SYSTEM and Administrators.
- Event source is `Application\Atlas Edge Runtime`; lifecycle IDs are 1500–1503.
- Secrets are not embedded; development mocks and production endpoints are absent.
- Installer logs contain acknowledgement flags (`1`) but no confidential value.
- Service recovery restarts after failures and resets its count after one day.

The service currently runs as **LocalSystem**. This is a pilot security-review
item and future hardening candidate. Review whether a dedicated virtual service
account can meet scanner, Event Log, credential, and ProgramData requirements
with materially less privilege.

## Signing and update plan

Production release requires Authenticode signing of the runtime executable, MSI,
and bootstrapper with externally protected keys and trusted timestamping. Verify
signatures and the SHA-256 manifest before deployment. A future updater must
require signed metadata and payloads, rollback protection, maintenance-window
policy, enterprise control, consent decisions, and incident revocation. No
updater is implemented or enabled now.

## Privacy and incident response

Review actual telemetry against the draft disclosure and customer allowlists.
Images, OCR/document content and metadata, unrelated personal data, credentials,
tokens, enrollment codes, and private keys are prohibited. Incident procedures
must define containment, service stop, evidence preservation, secret revocation,
customer notification decision-making, sanitized diagnostic collection, repair,
and release approval. Contacts remain **[INTERSCAN TO PROVIDE]**.

## Known limitations and questionnaire topics

Windows credential storage is incomplete; production enrollment/connectivity and
Windows installer execution remain unverified; legal drafts are unapproved; no
official ICO exists; and LocalSystem needs review. Customer questionnaires should
cover architecture/data flows, ports/domains/proxy/TLS, service account, ACLs,
software inventory/SBOM, vulnerability response, signing/key custody, updates,
logging/redaction, telemetry/retention/deletion, tenant isolation, incident
response, support access, uninstall retention, and subcontractor/cloud scope.

No compliance certification is claimed by this guide.
