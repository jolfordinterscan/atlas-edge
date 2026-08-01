# Enrollment Model

## Enrollment Goals

Enrollment establishes a trusted, tenant-scoped, revocable edge identity without hardcoded tenant or endpoint values.

## Required Elements

- One-time enrollment code
- Tenant binding
- Asset and scanner binding
- Device identity issuance
- Revocation support
- Credential rotation support
- No hardcoded tenant or endpoint
- US ingestion endpoint returned during enrollment
- Site timezone returned during enrollment

## Enrollment Outcome

Upon successful enrollment, Atlas Edge holds a per-device revocable identity and endpoint configuration required for outbound telemetry delivery.

## Local Mock Implementation Status

Secure enrollment is implemented in this repository against a local HTTPS mock API only.
No live Atlas Platform enrollment integration exists yet.

Runtime flow:

1. Runtime checks the credential store for a stored device identity.
2. If no identity exists, it reads the configured one-time enrollment code.
3. Runtime calls POST /api/edge/v1/enroll on the local mock API.
4. Enrollment response is validated for:
	- agent_id
	- device_id
	- tenant_binding
	- ingestion_url
	- site_timezone
	- access_token
	- refresh_token placeholder
	- credential_expiry_utc
5. Runtime persists identity and credentials in protected local development storage.
6. Enrollment code reuse is rejected by the local mock API.

Enrollment logs are sanitized and do not emit enrollment_code, access_token, or refresh_token values.

## Operational Notes

- Enrollment is a control-plane action and should be auditable
- Rotation and revocation events should produce audit records
- Failed enrollment attempts should avoid exposing sensitive values in logs
