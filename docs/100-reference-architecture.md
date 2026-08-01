# Reference Architecture

## Product And Runtime

Atlas Edge is the product.
The initial runtime implementation is a Windows Service on scanner-connected Windows workstations.

## Logical Flow

Scanner or Capture Application
-> Atlas Edge Runtime
-> Scanner Adapter
-> Telemetry Normalizer
-> Local Durable Queue
-> Secure Transport
-> Atlas Telemetry Ingestion
-> Tenant-Scoped Storage
-> Mission Control and Atlas Reasoning

## Responsibilities By Layer

- Scanner Adapter: Vendor or protocol-specific scanner/workstation observation
- Telemetry Normalizer: Convert observations into approved event contracts
- Local Durable Queue: Persist events safely for outage tolerance and replay-safe retry
- Secure Transport: Outbound TLS 443 delivery with retry, idempotency, and replay protection
- Atlas Telemetry Ingestion: Tenant-aware intake and validation boundary
- Tenant-Scoped Storage: Isolation boundary for telemetry retention and access
- Mission Control and Atlas Reasoning: Operator visibility and analytics/decision support

## Non-Functional Constraints

- No inbound listener
- No remote command execution in Version 1.0
- No interruption of scanning workflows
- Offline-capable recovery
