# Telemetry Contract

## Version 1.0 Intent

Atlas Edge 1.0 provides read-only operational telemetry from scanner-connected Windows workstations to Atlas Platform.

## MVP Event Families

- agent.enrolled
- agent.heartbeat
- agent.health
- workstation.inventory
- scanner.connected
- scanner.disconnected
- scanner.inventory
- scan.pages.recorded
- scan.job.started
- scan.job.completed
- scan.job.failed
- scanner.error

## Minimum Event Envelope

Every event must include:

- event_id
- event_type
- schema_version
- event_timestamp_utc
- observed_timestamp_utc
- agent_id
- workstation_id
- tenant binding
- asset or scanner identity when available
- source adapter
- correlation ID when applicable

## Explicitly Prohibited Data

- Document content
- Scanned images
- OCR text
- Customer document metadata
- Credentials
- Raw secrets

## Page-Count Position

- Existing internal prototypes support pages_today style heartbeat data.
- Reliable per-job and audit-grade page counting still requires one validated adapter and confirmed reset and duplex semantics.
- Atlas Edge 1.0 should ship with exactly one validated page-count source before claiming live page telemetry generally.
- Unsupported scanner models must report page-count capability as unavailable rather than estimate.

## Delivery Requirements

- Queue telemetry durably during outages
- Retry automatically with replay-safe delivery patterns
- Prevent duplicate page-count effects through idempotency and correlation
