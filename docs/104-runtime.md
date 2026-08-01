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

Runtime foundation code is implemented with local secure enrollment and authenticated batch heartbeat delivery.

Current behavior:

1. Runtime startup checks for stored credentials.
2. If identity is absent, runtime performs one-time enrollment against the local HTTPS mock API.
3. Enrollment response is validated and persisted to protected local development storage.
4. Runtime generates heartbeat events and queues them.
5. In HTTP transport mode, runtime posts authenticated event batches.
6. Accepted event IDs are acknowledged and removed from queue.
7. Retryable failures are retried.
8. Non-retryable failures are dropped safely with sanitized logging.
9. Runtime shuts down gracefully.

Null transport remains available for development fallback.
No live Atlas integration exists in this checkpoint.
