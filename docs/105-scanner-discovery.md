# Scanner Discovery

Atlas Edge performs bounded, read-only scanner inventory discovery independently of heartbeat processing. Discovery never connects to a scanner for acquisition, opens a vendor UI, moves a feeder, changes settings, captures images, counts pages, or executes commands.

## Runtime flow

1. `ScannerDiscoveryHostedService` honors the configured startup delay.
2. `ScannerDiscoveryService` invokes each enabled provider with an independent timeout.
3. Provider exceptions, unavailability, and timeouts become stable diagnostics; other providers continue.
4. Records are normalized, conservatively deduplicated, and published to `ScannerInventoryState`.
5. A deterministic fingerprint excludes observation timestamps and ordering.
6. In `QueueOnly` mode, a changed snapshot replaces the one local inventory event in the existing in-memory queue. An unchanged snapshot creates nothing.

The heartbeat queue batch API returns heartbeat entries only, so inventory cannot inflate heartbeat queue health or block a heartbeat. `Transport` mode uses the same authenticated batch endpoint only when a compatible Atlas Platform accepts `scanner.inventory` schema `1.0`. Permanent inventory rejection removes only that inventory; transient and authentication failures retain the latest coalesced snapshot.

## Normalized inventory

Each immutable record contains a stable scanner ID, hashed provider ID, manufacturer, model, optional serial and firmware, hashed device path where available, driver metadata, connection type, conservative operational status, explicitly supported capabilities, provider provenance, first/last observations, metadata-confidence category, and sanitized warnings. Strings are bounded to 256 characters during normalization.

Statuses are `Ready`, `Busy`, `Offline`, `Unavailable`, `Error`, or `Unknown`. WIA enumeration alone does not prove readiness, so WIA devices remain `Unknown` unless stronger evidence is added in a future checkpoint. Capabilities are emitted only when the provider supplies evidence. Unknown never becomes false, zero, ready, or healthy.

## Stable identity and deduplication

Identity uses the strongest available stable input:

1. Provider-stable identifier.
2. Manufacturer/model plus serial number when provider identity is absent.
3. SHA-256 of a normalized device path when the path is the strongest input.
4. Deterministic bounded provider/manufacturer/model/driver/interface metadata.

All public identifiers are truncated SHA-256 values prefixed with `scanner-`; random IDs are never used for scanner identity. Raw device paths do not enter normalized snapshots, logs, events, or probe output. Cross-provider records merge only on normalized manufacturer plus serial. Same-model devices with different or missing identities remain separate.

## Providers

WIA is the default and only provider intended to prove locally attached Windows scanner discovery in this checkpoint. It enumerates WIA `DeviceInfo` records of scanner type on a dedicated STA thread and reads optional properties without calling `Connect`, acquisition methods, dialogs, or item transfer APIs. COM objects are released in `finally` paths. A missing WIA runtime/service, access failure, malformed property, timeout, or disconnect is isolated and sanitized.

TWAIN and ISIS remain optional installed-source metadata boundaries inherited from the earlier connector checkpoint. They do not open data sources and cannot prove that installed devices are attached. They are disabled by default and are not vendor SDK integrations.

On non-Windows systems all native catalogs report unavailable. Production code never returns fake Windows devices. The development mock remains explicitly gated to `Development`.

## Configuration

```json
{
  "ScannerDiscoveryEnabled": true,
  "ScannerDiscoveryIntervalSeconds": 300,
  "ScannerDiscoveryStartupDelaySeconds": 5,
  "ScannerDiscoveryProviderTimeoutSeconds": 15,
  "ScannerDiscoveryProviders": [ "Wia" ],
  "ScannerInventoryPublishMode": "QueueOnly"
}
```

Intervals and timeouts are validated and bounded. `Disabled` creates no inventory event, `QueueOnly` retains one latest local event, and `Transport` submits that slot after runtime identity and authenticated transport are available.

## Inventory event contract

`scanner.inventory` schema `1.0` uses the existing event identity fields and includes `inventory_version`, `scanner_count`, and bounded normalized scanner entries. The inventory version is a full SHA-256 fingerprint over deterministically ordered meaningful fields. It excludes timestamps so identical snapshots remain unchanged. The event contains no raw COM objects, property bags, paths, tokens, enrollment codes, user names, document data, image data, or OCR text.

## Deferred work

- Real Windows/Ricoh hardware verification.
- Deployment of a compatible Atlas Platform scanner inventory schema is required before enabling `Transport`.
- Durable inventory queueing across process restart.
- Hot-plug notifications and provider-specific cancellation of a hung COM call.
- TWAIN DSM enumeration or vendor SDKs.
- Network/SNMP discovery, page counts, health transmission, acquisition, and commands.
