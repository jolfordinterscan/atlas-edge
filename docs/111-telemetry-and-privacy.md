# Atlas Edge telemetry and privacy

> **DRAFT FOR REVIEW — NOT APPROVED FOR PRODUCTION USE**

This engineering guide is not final legal or privacy language and makes no
claim of compliance certification.

## Purpose and categories

Atlas Edge is designed to observe operational health, not document content.
Depending on validated adapters, enabled capabilities, and customer policy, it
may process runtime/workstation identity, OS/runtime details, scanner identity
and supported metadata, connection state, health observations, discovery times,
operational status, validated page counts, queue health, service lifecycle and
heartbeat status, and sanitized policy-enabled diagnostic evidence.

Unknown, Unsupported, Unavailable, and Failed values remain distinct. Page
counts are collected only from a validated adapter with understood reset and
duplex semantics; unsupported models remain unavailable rather than estimated.

## Excluded content and diagnostic boundaries

Atlas Edge is not intended to collect scanned images, OCR text, document content,
customer document metadata, email, browser history, unrelated personal files,
passwords, authentication secrets, enrollment codes, bearer tokens, or private
keys. Diagnostic sources must be explicitly allowlisted, read-only, bounded by
size/read limits, sanitized, and authorized. Raw unrestricted logs, filesystem
crawling, registry crawling, TLS bypass, and credential guessing are prohibited.

## Responsibility and tenant boundaries

Customers define authorized devices, sites, capabilities, log/event sources,
retention expectations, access, and local policy. InterScan must document Atlas
ingestion, retention, access, deletion, and incident processes before production
connectivity. Local installer acceptance metadata is retained on uninstall and
is not legal proof beyond counsel-approved policy.

Tenant and device identities must remain scoped, revocable, and non-interchangeable.
No production Atlas path exists in this checkpoint. Current HTTPS enrollment and
transport work targets a local mock only.

## Security controls

- Outbound-only design; no inbound listener or remote command surface.
- TLS validation without bypass.
- Secret redaction and truncated SHA-256 diagnostic fingerprints.
- Atomic credential rotation through the credential-store abstraction.
- Protected Program Files and restricted ProgramData ACLs.
- Signed runtime/MSI/bootstrapper required for production release.
- Stable sanitized errors rather than raw response bodies.

Windows protected credential storage remains incomplete. LocalSystem service
identity requires pilot review and is a future least-privilege hardening candidate.

## Current versus planned

Current: local collection abstractions, mock-gated development evidence,
in-memory scanner states, local mock enrollment/transport, and null transport in
the installer baseline. Planned but not enabled: production connectivity,
approved retention operations, signed updates, automatic-update policy, and
broader validated adapters. Future capabilities require updated disclosure and
renewed legal, privacy, security, and customer review.
