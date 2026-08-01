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

This repository is at the secure local enrollment checkpoint stage.

- Product boundaries are documented
- Architecture and contracts are documented
- Event families are defined at a minimum-contract level
- Placeholder schemas are created with TODO markers
- A .NET 8 runtime foundation exists for development validation
- Secure enrollment is implemented against a local mock Atlas API over HTTPS
- Authenticated outbound heartbeat delivery is implemented for local development
- Access tokens refresh proactively with clock-skew tolerance and rotated credential persistence
- Expired access-token responses refresh and replay once; permanent authentication failures retain queued telemetry
- Read-only periodic scanner inventory discovery is implemented with WIA as the conservative default Windows provider
- Scanner inventory uses stable hashed identity, bounded normalized metadata, conservative status, provider timeouts, and failure isolation
- Changed inventories produce one coalesced local `scanner.inventory` event; the event is never sent because Atlas Platform currently accepts only heartbeats
- Read-only scanner health collection preserves unknown metrics and calculates evidence-based category scores
- macOS credential storage is development-only and uses protected local persistence
- Windows protected credential storage remains required before pilot release
- No live Atlas Platform integration exists yet
- Scanner health telemetry transmission, prediction, and automated remediation remain unimplemented

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

This repository contains a local secure enrollment and authenticated transport checkpoint.
Integration is intentionally limited to tools/Atlas.Edge.MockAtlasApi over local HTTPS.
Do not use this runtime against production Atlas endpoints.
Scanner commands, health telemetry transmission, prediction, automated remediation, migrations, and remote control remain out of scope.

## Local Mock HTTPS Runbook

1. Trust the ASP.NET Core development certificate if needed:
	- dotnet dev-certs https --trust
2. Choose a local one-time enrollment code. Do not save it in committed configuration.
3. Start the local mock API on HTTPS, providing the code through an environment variable:
	- `ATLAS_MOCK_MockAtlas__DevelopmentEnrollmentCode=<your-local-code> dotnet run --project tools/Atlas.Edge.MockAtlasApi/Atlas.Edge.MockAtlasApi.csproj --launch-profile https`
4. Start Atlas Edge in HTTP transport mode, providing the same code locally:
	- `ATLAS_EDGE_AtlasEdge__TransportMode=Http ATLAS_EDGE_AtlasEdge__EnrollmentCode=<your-local-code> dotnet run --project src/Atlas.Edge.Runtime/Atlas.Edge.Runtime.csproj`

HTTPS is required by default for enrollment and ingestion. For an HTTP-only local endpoint, developers must set both `AtlasEdge:EnvironmentName` to `Development` and `AtlasEdge:AllowInsecureHttpForDevelopment` to `true`; this override is rejected in every other environment.

Token refresh defaults to five minutes before access-token expiry with 30 seconds of clock-skew tolerance. Refresh timing and bounded retry settings are available under the `AtlasEdge` configuration section. Raw tokens, Authorization headers, and response bodies are never logged; diagnostics use truncated SHA-256 fingerprints and stable error codes.

Scanner discovery runs independently at a bounded interval when `AtlasEdge:ScannerDiscoveryEnabled` is `true`. The default `Platform` mode enables only WIA; optional installed-source TWAIN and ISIS metadata providers must be explicitly listed in `ScannerDiscoveryProviders`. The `Mock` provider returns an obvious local test device and is rejected unless `AtlasEdge:EnvironmentName` is `Development`. `ScannerInventoryPublishMode` defaults to `QueueOnly`, which coalesces the latest changed inventory inside the local queue while heartbeat batches remain transportable. `Transport` is rejected until Atlas Platform supports `scanner.inventory`.

On Windows, run the safe read-only probe with `dotnet run -c Release --project tools/Atlas.Edge.ScannerProbe/Atlas.Edge.ScannerProbe.csproj`. It enumerates WIA scanner-class devices, masks serial numbers, does not open a device or acquisition session, and never transmits.

Scanner health collection also runs once at startup when `AtlasEdge:ScannerHealthEnabled` is `true`. Platform providers retain absent or invalid metrics as unknown. The health `Mock` provider is restricted to Development and publishes only obvious synthetic values.
