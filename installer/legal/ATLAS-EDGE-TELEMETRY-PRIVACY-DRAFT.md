# Atlas Edge Telemetry and Privacy Disclosure

> **DRAFT FOR REVIEW — NOT APPROVED FOR PRODUCTION USE**

Document version: **DRAFT-0.1**

> Atlas Edge is designed to observe operational health, not document content.

This document is a review draft, not final legal or privacy language. InterScan
legal counsel and authorized product/security reviewers must approve the final
disclosure before production distribution.

## Operational telemetry that may be collected

Subject to enabled capabilities, validated adapters, and customer policy, Atlas
Edge may collect authorized operational telemetry such as:

- Atlas Edge runtime identity and version
- Workstation identity
- Operating system and runtime information
- Scanner identity and supported device metadata
- Scanner connection state
- Scanner health observations
- Scanner discovery timestamps
- Operational status
- Page counts when a validated adapter supports them
- Queue health
- Service lifecycle and heartbeat status
- Sanitized errors and diagnostic evidence when enabled by policy

Unknown or unsupported values must remain Unknown or Unsupported. Atlas Edge
must not estimate page counts or claim that a capability is active when it is
not available.

## Content Atlas Edge is not intended to collect

Atlas Edge is not intended to collect:

- Scanned document images
- OCR text
- Document contents
- Customer document metadata
- Email content
- Browser history
- Unrelated personal files
- User passwords
- Authentication secrets
- Enrollment codes
- Bearer tokens
- Private cryptographic keys

## Scope and authorization caveats

- Exact telemetry depends on enabled capabilities and customer policy.
- Diagnostic collection must be authorized and scoped.
- Future capabilities may require updated disclosure and renewed review.
- This draft is not final legal or privacy language.
- Local logs and evidence references must remain allowlisted, bounded, and
  sanitized; unrestricted content ingestion is outside this checkpoint.
- Retention, access, and deletion obligations require a documented customer and
  Atlas operating policy before production connectivity.

## Future software updates

Atlas Edge may support digitally signed software updates in a future release.
Automatic update behavior is not enabled by this installer foundation. Update
policy, consent, maintenance windows, rollback, signing, and enterprise
administration remain future work.

## Required installer acknowledgement

> I understand the operational telemetry and privacy disclosure.

The installer must require explicit acknowledgement before continuing. This
acknowledgement is not a substitute for customer authorization, applicable
notice, contract terms, or counsel-approved privacy language.
