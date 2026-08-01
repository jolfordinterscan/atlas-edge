# Atlas Edge

## What Atlas Edge Is

Atlas Edge is the edge-side product that securely extends InterScan Atlas into customer environments.
It runs on scanner-connected Windows workstations, collects trusted scanner and workstation telemetry,
and delivers telemetry to Atlas over resilient outbound HTTPS.

Atlas Edge 1.0 scope:

- United States focused
- Windows 10 and Windows 11
- Installed on scanner-connected workstations
- Outbound HTTPS only
- Read-only telemetry initially
- Offline capable
- Tenant and asset scoped
- Designed to avoid interrupting scanning

## How Atlas Edge Fits With Atlas Platform

Atlas Edge is the in-environment telemetry producer.
Atlas Platform is the cloud-side telemetry ingestion, storage, monitoring, and reasoning layer.

Atlas Edge is responsible for:

- Device enrollment and identity bootstrap
- Local telemetry collection and normalization
- Durable queueing during outages
- Secure, retrying outbound delivery

Atlas Platform is responsible for:

- US ingestion endpoints
- Tenant-scoped storage
- Mission Control visibility
- Atlas reasoning and downstream analytics

## Current Status

This repository is at the foundation checkpoint stage.

- Product boundaries are documented
- Architecture and contracts are documented
- Event families are defined at a minimum-contract level
- Placeholder schemas are created with TODO markers
- No production runtime implementation exists yet

## US-First Deployment Scope

Atlas Edge 1.0 targets US customer deployment first.
Enrollment must return a US ingestion endpoint and site timezone.
No hardcoded tenant or endpoint is allowed.

## Security And Privacy Boundaries

Atlas Edge is non-negotiably constrained to:

- Never collect document contents, scanned images, OCR text, or customer document metadata
- Never require inbound firewall access
- Never use a global shared device secret
- Use per-device revocable identity and outbound TLS on port 443
- Keep credentials in Windows-protected secure storage
- Redact sensitive data in logs
- Keep remote command execution out of scope for Version 1.0

## Repository Map

See [REPOSITORY-MAP.md](REPOSITORY-MAP.md) for the complete repository map.

## Important Note

This repository currently contains product and architectural groundwork only.
No production code is implemented yet.
