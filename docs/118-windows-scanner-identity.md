# Windows Scanner Identity Enrichment

Atlas Edge enriches scanners already discovered through WIA. It does not use
Windows metadata to invent or independently create scanner inventory records.
This checkpoint is read-only and contains no acquisition, page-count, command,
configuration, service-control, or registry-write surface.

## Precedence and correlation

The enrichment order is:

1. Windows Plug and Play metadata under the allowlisted USB enumeration root.
2. The allowlisted Windows Still Image device registry root.
3. Existing WIA and installed-source TWAIN identity metadata.
4. A vendor metadata provider interface with no implementation.

Correlation requires a strong device-instance or USB VID/PID match. Friendly
name, model, and manufacturer add supporting weight but cannot establish a
match alone. Equal best matches are treated as ambiguous and ignored. Scanner
IDs continue to come from the existing deterministic identity factory, so
enrichment does not change disconnect/reconnect identity.

## Normalized fields

When exposed, Atlas records serial and source, friendly name, driver name,
provider and version, USB VID/PID, and SHA-256 identifiers for hardware ID,
container ID, location path, and device instance ID. Missing fields remain
Unknown. Values are bounded before entering inventory.

PnP USB instance suffixes are accepted as serial evidence only when Windows
provides a stable-looking suffix without topology separators. This is source
evidence, not inference from manufacturer or model. Normal logs and the scanner
probe mask serials as `****1234`.

## Privacy and safety

- Device paths, instance IDs, registry paths, hardware IDs, container IDs, and
  location paths are never copied raw into normalized snapshots or events.
- Sensitive identifiers are normalized and SHA-256 hashed at the provider
  boundary.
- Registry access is read-only and restricted to fixed PnP, driver-class, and
  Still Image roots. Dynamic driver subkeys must match the Windows class-driver
  reference format.
- Provider exceptions become absence/diagnostics and do not expose platform
  exception text.
- Provider calls use the configured scanner discovery provider timeout and do
  not alter heartbeat timing.
- TWAIN remains installed-source identity inspection only. Atlas never opens a
  TWAIN source, displays UI, or calls transfer APIs.

## Windows limitations

Windows and a vendor driver may omit serial, firmware, capabilities, readiness,
or driver metadata. USB instance suffixes are not always device serials, and
network or virtual WIA sources may not correlate with the USB PnP tree. Registry
layout and driver packaging vary by vendor. In every case absence remains
Unknown rather than being inferred.

The implementation compiles and its normalization is tested cross-platform.
Actual FUJITSU fi-8170, InoTec SCAMAX, and Canon MF620C enrichment must be
verified with the scanner probe on Jeremy's Windows workstation. Record which
source exposed each value; if serial remains Unknown, report PnP and Still Image
registry results separately without fabricating a value.
