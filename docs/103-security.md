# Security Model

## Core Controls

- Outbound TLS on port 443
- Windows-protected credential storage
- Per-device revocable identity
- Signed installer and binaries
- Signed updates
- Least-privilege Windows service
- Log redaction
- Replay protection
- Idempotency
- Audit trail
- No inbound listener
- No remote command execution in Version 1.0

## Identity And Credential Principles

- No global shared device secret
- Credentials are tenant and device scoped
- Revocation and rotation are first-class behaviors

## Current Implementation Notes

- Secure enrollment and authenticated outbound transport are implemented against a local HTTPS mock API only.
- No production Atlas credential paths are implemented in this checkpoint.
- macOS uses a protected local development credential store with encrypted-at-rest payloads.
- Windows protected credential storage is still a required gap before pilot release.
- No plaintext credentials are committed to source.
- Secret redaction helpers are used for log-safe token fingerprints only.
- Token fingerprints are truncated SHA-256 digests and never expose token prefixes or suffixes.
- Credential lifecycle states are Unenrolled, Active, Refreshing, and AuthenticationRequired.
- Refresh-token rotation is serialized in-process and published only after protected persistence succeeds.
- Runtime does not expose any inbound listener.

## Logging And Privacy

- Logs must avoid raw secrets and credentials
- Telemetry payloads must exclude document content, scanned images, OCR text, and customer document metadata

## Trust Boundary

Atlas Edge is an outbound telemetry sender.
It is not a remote execution endpoint in Version 1.0.
