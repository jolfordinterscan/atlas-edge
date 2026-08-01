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

## Operational Notes

- Enrollment is a control-plane action and should be auditable
- Rotation and revocation events should produce audit records
- Failed enrollment attempts should avoid exposing sensitive values in logs
