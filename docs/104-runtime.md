# Runtime Model

## Runtime Implementation For Version 1.0

The initial Atlas Edge runtime implementation is a Windows Service running on scanner-connected Windows 10 and Windows 11 workstations.

## Runtime Behavior Requirements

- Never interrupt scanner operation
- Remain quiet by design during normal operation
- Continue operating through network outages
- Queue telemetry durably and retry automatically
- Recover automatically after transient failures
- Preserve tenant and asset scoping across restarts

## Transport Requirements

- Outbound HTTPS only on port 443
- No inbound listener
- Retry and idempotency semantics for safe re-delivery

## Version 1.0 Limits

- Read-only telemetry only
- No remote command execution
- No scanner control operations

## Implementation Status

No production Windows Service code is implemented in this repository at the foundation checkpoint stage.
