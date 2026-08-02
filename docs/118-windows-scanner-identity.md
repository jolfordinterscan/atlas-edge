# Windows Scanner Identity Enrichment

Atlas Edge enriches scanners already discovered through WIA. It does not use
Windows metadata to invent or independently create scanner inventory records.
This checkpoint is read-only and contains no acquisition, page-count, command,
configuration, service-control, or registry-write surface.

## Precedence and correlation

The metadata source order is:

1. Windows Plug and Play metadata under the allowlisted USB enumeration root.
2. The allowlisted Windows Still Image device registry root.
3. Existing WIA and installed-source TWAIN identity metadata.
4. A vendor metadata provider interface with no implementation.

Within each source, correlation uses this strict precedence:

1. Exact device-instance identity.
2. Exact USB VID/PID identity.
3. Unique normalized manufacturer plus model/friendly name.
4. Unique manufacturer plus the Windows `usbscan` scanner-class service.

Atlas stops at the first strategy with candidates. That strategy must identify
exactly one record. Equal candidates are ambiguous and ignored; a lower-ranked
strategy cannot break the tie. Manufacturer alone is never sufficient. Model
alone is never sufficient.

Comparison trims and folds case and whitespace. A manufacturer prefix may be
removed from the model only for comparison, allowing `FUJITSU fi-8170` to match
`fi-8170`. A terminal driver-added number such as `#3` is likewise ignored only
for comparison. Stored names are not rewritten. Scanner IDs continue to come
from the existing deterministic identity factory, so enrichment does not
change disconnect/reconnect identity.

## Normalized fields

When exposed, Atlas records serial and source, friendly name, driver name,
provider and version, USB VID/PID, and SHA-256 identifiers for hardware ID,
container ID, location path, and device instance ID. Missing fields remain
Unknown. Values are bounded before entering inventory.

PnP USB instance suffixes are accepted as serial evidence only when Windows
provides a stable-looking suffix containing only bounded letters, digits,
periods, underscores, or hyphens. Topology-generated suffixes such as
`6&3A91DD4C&0&2` and any value containing `&` are rejected. This is source
evidence, not inference from manufacturer or model. Normal logs and the scanner
probe mask serials as `****1234`.

When a unique PnP match supplies driver name, provider, or version, those values
take precedence over weaker WIA or Still Image values. A missing PnP value does
not erase a known existing value. For the observed fi-8170 evidence this means
the PnP driver version `2.0.0.9` supersedes WIA's `2.0.0.4`.

## Observed FUJITSU fi-8170 evidence

Jeremy's Windows workstation exposed one scanner-class PnP record for the WIA
device `FUJITSU` / `fi-8170`. Its hardware IDs identify USB VID `04C5` and PID
`15FF`; Windows also exposes the `usbscan` service, driver version `2.0.0.9`, a
container ID, location paths, and a device instance ID. Atlas hashes the latter
identifiers before they enter the normalized snapshot.

The instance suffix `6&3A91DD4C&0&2` describes USB topology and is not a serial.
Serial therefore remains Unknown unless a future authoritative Windows or
vendor metadata source exposes one.

Expected post-fix probe fields are:

```text
Manufacturer: FUJITSU
Model: fi-8170
Serial: Unknown
Serial source: Unknown
Friendly name: fi-8170
Driver: fi-8170
Driver provider: FUJITSU
Driver version: 2.0.0.9
USB VID: 04C5
USB PID: 15FF
Location hash: <64-character SHA-256>
Container ID hash: <64-character SHA-256>
Device instance ID hash: <64-character SHA-256>
Connection: Usb
Status: Unknown
Capabilities: Unknown
Stable Scanner ID: scanner-ca9cbc762608af46bece7e18
```

This expected result must still be verified on Windows. It is not evidence that
the macOS development host accessed the device.

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
- `--metadata-diagnostics` is opt-in and prints only provider name, match
  strategy, numeric score, candidate count, ambiguity, and populated field
  names. It never prints raw PnP, registry, hardware, container, or location
  identifiers.
- TWAIN remains installed-source identity inspection only. Atlas never opens a
  TWAIN source, displays UI, or calls transfer APIs.

## Windows limitations

Windows and a vendor driver may omit serial, firmware, capabilities, readiness,
or driver metadata. USB instance suffixes are not always device serials, and
network or virtual WIA sources may not correlate with the USB PnP tree. Registry
layout and driver packaging vary by vendor. In every case absence remains
Unknown rather than being inferred.

The implementation compiles and its normalization is tested cross-platform.
The correlation fix compiles and is tested cross-platform, but the post-fix
FUJITSU fi-8170, InoTec SCAMAX, and Canon MF620C enrichment must be verified
again with the scanner probe on Jeremy's Windows workstation. If serial remains
Unknown, the next safe route is an optional vendor metadata provider—not
topology inference.

Run the normal and diagnostic probes from PowerShell:

```powershell
git pull --ff-only
dotnet build -c Release
dotnet test -c Release
dotnet run -c Release --project .\tools\Atlas.Edge.ScannerProbe\Atlas.Edge.ScannerProbe.csproj
dotnet run -c Release --project .\tools\Atlas.Edge.ScannerProbe\Atlas.Edge.ScannerProbe.csproj -- --metadata-diagnostics
```
