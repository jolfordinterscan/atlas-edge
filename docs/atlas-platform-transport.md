# Atlas Platform Transport

Atlas Edge is an outbound-only Windows service. It enrolls with an explicitly
configured Atlas staging endpoint, stores rotated credentials through the
credential-store abstraction, and sends authenticated HTTPS batches. No
production Atlas URL is compiled into the runtime.

## Endpoint and enrollment configuration

Set runtime values with `ATLAS_EDGE_AtlasEdge__...` environment variables or a
protected local configuration source. For a compatible staging deployment:

```text
TransportMode=Http
EnrollmentUrl=https://<staging-host>/api/edge/v1/enroll
IngestionUrl=https://<staging-host>/api/edge/v1/events/batch
ScannerInventoryPublishMode=Transport
HeartbeatIntervalSeconds=60
ScannerInventoryReconciliationIntervalSeconds=86400
```

HTTPS is mandatory except for the explicit Development-only localhost override.
Enrollment yields a tenant/device-bound lease with independently expiring access
and refresh tokens. Refresh rotation is single-flight; invalid or revoked
refresh credentials pause transmission without stopping discovery or dropping
queued events. Tokens, codes, authorization headers, and response bodies are
never written to telemetry payloads or normal logs.

## Heartbeats

The worker creates schema `1.0` `agent.heartbeat` events at the configured
interval (60 seconds by default). Each event includes the stable agent and
workstation bindings, Edge version, service state, and queue pending/in-flight
counts. The durable queue assigns a receipt independently of the event ID.
Accepted event IDs remove their receipts; partial acceptance retries only the
remaining receipts.

Retryable network and server failures use bounded exponential backoff. The
default starts at five seconds and caps at five minutes. An explicit
`access_token_expired` response permits one refresh and one replay. Generic
401/403 responses do not trigger blind refresh.

## Scanner inventory

Read-only discovery builds schema `1.0` `scanner.inventory` snapshots after
startup and on the configured discovery interval. The normalized content hash
excludes observation timestamps. Changed identity or metadata, scanner removal,
and scanner restoration create a new pending event. Unchanged accepted
inventory is suppressed, with a default 24-hour forced reconciliation so Atlas
can confirm the complete current attachment set.

The queue holds one latest inventory slot. A newer changed snapshot coalesces
the prior unsent snapshot; it cannot grow without bound. A pending identical
snapshot is never replaced, even during forced reconciliation. Permanent schema
or validation rejection clears only the inventory slot and cannot poison the
heartbeat queue. Transient failures leave the latest inventory pending.

`QueueOnly` remains the conservative default for deployments whose Atlas
Platform compatibility has not been established. Set `Transport` only for a
platform that accepts `/api/edge/v1/events/batch` scanner inventory schema 1.0.

## Durable offline behavior

Authenticated HTTP mode uses an atomic JSON queue at:

```text
C:\ProgramData\InterScan\Atlas Edge\queue\outbound-events.json
```

The location is configurable with an absolute `EventQueueStorePath`. The queue
persists event IDs, retry attempts, availability times, heartbeat payloads, the
coalesced inventory, and its last acknowledged fingerprint. It does not persist
credentials or enrollment codes. On restart, in-flight receipts return to
pending and retain their idempotent event IDs.

Heartbeat retention defaults to seven days and capacity to 10,000 events. At
capacity the oldest non-in-flight heartbeat is removed before a new heartbeat
is appended. This is a bounded availability policy, not guaranteed indefinite
offline retention. Inventory uses a separate single-slot bound.

## Privacy and payload boundaries

Inventory contains normalized scanner metadata only. Raw device paths, raw PnP
instance IDs, registry paths, usernames, document names/content, images, OCR,
and secrets are absent. Machine-level scanner identifiers are SHA-256 hashes.
Serials remain optional and scanner identity stays the stable Edge-generated
scanner ID if an authoritative serial is learned later.

## Local verification

The normal repository suite does not require a cloud connection:

```bash
dotnet restore
dotnet build -c Release
dotnet test -c Release
dotnet format --verify-no-changes
git diff --check
```

For a local compatible Atlas API, configure the Development-only HTTP override,
use a local enrollment code, set `TransportMode=Http` and
`ScannerInventoryPublishMode=Transport`, then stop and restart the API to prove
retry/replay. Never use the override for a non-loopback address.

## Troubleshooting

- `https_required`: correct the endpoint or use only the explicit localhost
  Development override.
- `credential_unavailable`: enrollment has not completed or persisted identity
  cannot be loaded.
- `authentication_required`: refresh credentials are invalid, expired, or
  revoked; discovery continues and events remain queued.
- `transport_network_error` / `transport_timeout`: Atlas is unavailable; the
  event remains queued according to its retry policy.
- permanent inventory validation errors: verify platform schema compatibility;
  heartbeats continue independently.

## Current limitations

- Windows credential storage remains a documented placeholder and requires
  production hardening before broad deployment.
- The queue is a bounded local JSON repository, not a transactional database.
- Inventory has a durable coalesced latest-state slot rather than durable local
  history.
- No page counts, image acquisition, scanner commands, remote control, or AI
  behavior is part of this transport.
