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

## Logging And Privacy

- Logs must avoid raw secrets and credentials
- Telemetry payloads must exclude document content, scanned images, OCR text, and customer document metadata

## Trust Boundary

Atlas Edge is an outbound telemetry sender.
It is not a remote execution endpoint in Version 1.0.
