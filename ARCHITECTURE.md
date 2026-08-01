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

This repository documents contracts and architecture only.
No production service implementation is included yet.
