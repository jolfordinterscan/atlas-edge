# Repository Map

## Purpose

This file maps Atlas Edge repository contents at the foundation checkpoint.

## Root

- README.md: Project overview, scope, and boundaries
- PRODUCT.md: Product definition and non-negotiables
- ARCHITECTURE.md: System architecture and flow
- ROADMAP.md: Versioned delivery checkpoints
- REPOSITORY-MAP.md: Repository structure map
- VERSION.md: Current checkpoint version and status
- .gitignore: Ignore policy

## Primary Directories

- core/: Reserved for future core runtime implementation assets
- docs/: Product and architecture reference documentation
- enrollment/: Reserved for enrollment collateral and workflows
- installer/: Reserved for installer packaging assets
- samples/: Reserved for examples and non-production samples
- schemas/: Placeholder JSON schemas for event and enrollment contracts
- src/: Architectural source layout for future implementation
- tests/: Reserved for validation and test assets
- tools/: Reserved for development and validation tooling

## Docs Directory

- docs/000-foundational-principles.md
- docs/001-executive-overview.md
- docs/100-reference-architecture.md
- docs/101-telemetry.md
- docs/102-enrollment.md
- docs/103-security.md
- docs/104-runtime.md
- docs/105-roadmap.md

## Source Structure

- src/adapters/
- src/configuration/
- src/diagnostics/
- src/enrollment/
- src/identity/
- src/queue/
- src/runtime-engine/
- src/security/
- src/telemetry/
- src/transport/
- src/updates/
- src/utilities/

## Schemas

- schemas/agent-heartbeat.schema.json
- schemas/agent-health.schema.json
- schemas/workstation-inventory.schema.json
- schemas/scanner-inventory.schema.json
- schemas/scan-pages-recorded.schema.json
- schemas/scan-job.schema.json
- schemas/scanner-error.schema.json
- schemas/enrollment.schema.json