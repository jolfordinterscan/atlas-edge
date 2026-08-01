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
- macOS credential storage is development-only and uses protected local persistence
- Windows protected credential storage remains required before pilot release
- No live Atlas Platform integration exists yet
- Scanner discovery and page counts remain unimplemented

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
Scanner discovery, page-count collection, migrations, and remote control remain out of scope.

## Local Mock HTTPS Runbook

1. Trust the ASP.NET Core development certificate if needed:
	- dotnet dev-certs https --trust
2. Choose a local one-time enrollment code. Do not save it in committed configuration.
3. Start the local mock API on HTTPS, providing the code through an environment variable:
	- `ATLAS_MOCK_MockAtlas__DevelopmentEnrollmentCode=<your-local-code> dotnet run --project tools/Atlas.Edge.MockAtlasApi/Atlas.Edge.MockAtlasApi.csproj --launch-profile https`
4. Start Atlas Edge in HTTP transport mode, providing the same code locally:
	- `ATLAS_EDGE_AtlasEdge__TransportMode=Http ATLAS_EDGE_AtlasEdge__EnrollmentCode=<your-local-code> dotnet run --project src/Atlas.Edge.Runtime/Atlas.Edge.Runtime.csproj`

HTTPS is required by default for enrollment and ingestion. For an HTTP-only local endpoint, developers must set both `AtlasEdge:EnvironmentName` to `Development` and `AtlasEdge:AllowInsecureHttpForDevelopment` to `true`; this override is rejected in every other environment.
