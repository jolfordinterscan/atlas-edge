# Atlas Edge Architecture

## Product Versus Runtime Implementation

Atlas Edge is the product.
The initial runtime implementation is a Windows Service for Windows 10 and Windows 11 scanner-connected workstations.

## Reference Logical Flow

Scanner or Capture Application
-> Atlas Edge Runtime
-> Scanner Adapter
-> Telemetry Normalizer
-> Local Durable Queue
-> Secure Transport
-> Atlas Telemetry Ingestion
-> Tenant-Scoped Storage
-> Mission Control and Atlas Reasoning

## Architecture Priorities

- Reliable outbound-only operation over HTTPS on port 443
- Durable local queueing and automatic retry during outages
- Per-device identity, tenant binding, and revocable credentials
- Idempotent delivery and replay protection
- Runtime behavior that remains quiet and non-disruptive to scanning workflows

## Security And Privacy Constraints

- No inbound listener
- No remote command execution in Version 1.0
- No collection of document contents, scanned images, OCR text, or customer document metadata
- No global shared device secret

## Current Stage

This repository now includes a .NET 8 runtime checkpoint for local secure enrollment and authenticated outbound delivery.
The runtime supports configuration validation, one-time enrollment against a local HTTPS mock API, protected local development credential storage on macOS, proactive single-flight token refresh, rotated credential persistence, read-only scanner inventory discovery and health collection, heartbeat generation, in-memory queueing, authenticated HTTP batch send, retry classification, and graceful shutdown.

Scanner discovery is isolated in `Atlas.Edge.ScannerDiscovery`. WIA is the default real Windows provider and enumerates scanner-class metadata only on a dedicated STA thread. Provider calls have bounded timeouts and isolated failure codes. Stable identity prefers provider identity, then manufacturer/model plus serial, then a hashed device path, then deterministic bounded metadata. Records with the same manufacturer and serial number may merge across providers; matching model names alone never merge. Unknown capability and status evidence remains unknown.

`IScannerMetadataEnricher` runs after read-only WIA enumeration and before normalization. Injectable PnP and allowlisted registry providers correlate conservatively using ordered exact-instance, exact-VID/PID, unique manufacturer/model, and unique manufacturer/scanner-class strategies. A strategy must identify exactly one candidate; ties remain Unknown. Providers isolate failures and timeouts and return only bounded metadata plus SHA-256 identifiers. Matched PnP driver values override weaker WIA driver values, while absent PnP values preserve known WIA metadata. They never write the registry, open a scan session, load vendor code, or change the stable scanner identity. Vendor SDK support is an interface boundary only.

`ScannerDiscoveryHostedService` runs independently from heartbeat generation, updates immutable local inventory state, and creates a versioned `scanner.inventory` event only when the deterministic snapshot fingerprint changes. The in-memory queue keeps exactly one latest inventory event in a separate coalesced slot. Heartbeat batching cannot see that slot, so inventory cannot block or poison heartbeat delivery. `QueueOnly` retains the slot locally. `Transport` offers it to an authenticated Atlas Platform that supports schema `1.0`; acceptance clears it, transient/authentication failures retain it, and a permanent validation rejection clears only inventory while heartbeat delivery continues.

Scanner health is isolated in `Atlas.Edge.ScannerHealth`. Providers consume read-only source metadata and publish normalized immutable startup snapshots. Unknown values remain unknown. Mechanical, reliability, performance, connectivity, and overall scores are calculated only when their required evidence is available. Health snapshots remain local and are not queued or transmitted.

The following remain intentionally unimplemented at this checkpoint:

- Live Atlas Platform integration
- Secure per-device production storage for Windows
- Scanner health telemetry transmission
- Prediction and automated remediation
