# Atlas Edge Product Definition

## Product Statement

Atlas Edge securely extends InterScan Atlas into customer environments by collecting trusted scanner and workstation telemetry and delivering it to Atlas through resilient outbound HTTPS communication.

## Version 1.0 Product Boundaries

Atlas Edge 1.0 is:

- United States focused
- Windows 10 and Windows 11
- Installed on scanner-connected workstations
- Outbound HTTPS only
- Read-only telemetry initially
- Offline capable
- Tenant and asset scoped
- Designed not to interrupt scanning

## Out Of Scope For Version 1.0

- Remote control of scanners or workstations
- Inbound listeners or inbound command channels
- Document content, scanned images, OCR text, or customer document metadata collection
- Production claims for universal page-count support across all scanner models

## Foundational Non-Negotiables

- Never interrupt scanner operation
- Never collect document contents or scanned images
- Never require inbound firewall access
- Never use one global shared device secret
- Continue operating during network outages
- Queue telemetry safely and retry automatically
- Prevent duplicate page counts
- Secure by default
- Quiet by design
- Recover automatically
- Humans control consequential actions
- Remote control is out of scope for Version 1.0

## Initial Platform Relationship

Atlas Edge is the edge runtime product.
Atlas Platform is the cloud system that ingests telemetry, stores it in tenant-scoped form, and powers Mission Control and Atlas reasoning.
