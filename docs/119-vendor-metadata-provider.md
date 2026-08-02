# Vendor Metadata Provider Foundation

Atlas Edge can now inventory likely scanner-vendor software without loading or
executing it. This checkpoint does not read proprietary scanner metadata. It
creates the safe discovery and capability contracts needed to evaluate a
licensed, documented vendor interface later.

## Safety boundary

The catalog is Windows-only and read-only. It does not:

- start vendor processes or command-line tools;
- load DLLs, type libraries, or COM classes;
- register COM components;
- inspect DLL exports or reverse engineer binaries;
- connect to named pipes or local services;
- read undocumented configuration payloads;
- change `PATH`, scanner settings, firmware, counters, or the registry;
- initiate acquisition, transfer images, or issue scanner commands.

File inspection is limited to names, paths, architecture inferred from known
Windows roots, and version resources. Directory traversal is bounded to known
Program Files roots, limited in depth and count, and does not follow reparse
points. Registry inspection uses fixed installed-program, service, COM, WIA,
and driver registration roots with read-only handles.

## Installation catalog

`VendorInstallationCatalog` aggregates injectable sources and returns immutable
records containing:

- vendor and product name;
- detected version when exposed;
- installation path;
- x86, x64, AnyCPU, or Unknown architecture;
- discovery source;
- bounded SDK/interface candidates.

The Windows source recognizes PaperStream, Ricoh, PFU, Fujitsu fi Series,
ScanSnap, TWAIN DSM, and WIA component markers. Candidate `.dll`, `.ocx`,
`.tlb`, and `.exe` files are classified only by extension and conservative name
markers. A candidate is not proof that an API is licensed, documented, safe,
or compatible.

## Provider architecture

`IVendorMetadataProvider` reports provider and per-field capability state:

- `Available`: the provider or field is implemented and usable;
- `Unavailable`: required installed software was not detected;
- `Unsupported`: software may exist, but Atlas has no approved implementation;
- `Unknown`: capability could not be established.

The implemented provider classes are:

- `NoOpVendorMetadataProvider`;
- `PaperStreamMetadataProvider` stub;
- `RicohMetadataProvider` stub;
- `PFUMetadataProvider` stub;
- `VendorMetadataProviderFactory`.

The three vendor stubs report provider availability when matching software is
installed, but every metadata field remains `Unsupported` with the stable code
`vendor_adapter_not_implemented`. They expose no scanner read method and are not
registered with runtime scanner enrichment.

## Public interface research

Research was limited to public Ricoh/PFU material. No binary inspection,
protocol probing, or license bypass was performed.

| Component | Publicly documented purpose | Interface evidence | Metadata-only status |
|---|---|---|---|
| PaperStream IP | TWAIN/ISIS scanner driver and image processing | Standard acquisition-driver interfaces | No approved metadata-only serial/counter interface identified |
| RICOH Scanner Control SDK | Application development around fi Series scanners | Official SDK history describes native transfer and managed class-library support | Candidate for licensed evaluation; safe read-only metadata calls not yet verified |
| fi Series Web API | Web application integration for supported fi Series models | Requires RICOH Scanner Control Runtime and PaperStream IP; SDK access is inquiry-based | Not adopted; local hosting/acquisition implications require separate security review |
| Scanner Central Admin / PaperStream Central Admin | Fleet monitoring, firmware/software management, errors, and consumables | Product UI/server-agent functionality is documented | No documented local read-only integration API identified in reviewed material |
| Software Operation Panel | Displays lifetime and consumable counters and allows counter reset/settings | Installed Windows utility/UI | Values are relevant, but no documented read-only application API was identified |
| Installed COM registrations | Windows component registration | Catalog can identify matching in-process server files | Unknown until vendor documentation and licensing establish a supported contract |
| Installed DLLs | Driver/application components | Catalog identifies bounded candidates without loading them | Unknown; exports are intentionally not inspected |
| Installed executables | Applications or possible CLI candidates | Catalog records names and versions without execution | Unknown; Atlas does not execute them |
| Registry/configuration/named pipes | Possible internal integration surfaces | No approved public contract identified | Unsupported for implementation; no probing is performed |

Official references reviewed:

- [PaperStream IP product and supported-scanner information](https://www.pfu.ricoh.com/global/scanners/fi/psip/)
- [RICOH Scanner Control SDK change history](https://www.pfu.ricoh.com/global/scanners/fi/support/software/sdk-history.html)
- [fi Series Web API environment](https://www.pfu.ricoh.com/fi/software/fi-series-web-api/environment.html)
- [Scanner Central Admin product information](https://www.pfu.ricoh.com/global/scanners/fi/software/ps-ip/ps-ip.html)
- [fi-81x0 manual downloads](https://www.pfu.ricoh.com/global/scanners/fi/support/manuals/fi-81x0.html)

These references confirm that relevant counters, firmware, errors, and
consumable information exist in Ricoh/PFU tools and scanner interfaces. They do
not establish that Atlas may retrieve those values through an installed local
API without additional documentation, licensing, consent, and technical review.

## Metadata matrix

| Field | Generic Windows result | Vendor foundation result | Future requirement |
|---|---|---|---|
| Serial number | Unknown for the validated fi-8170 | Unsupported | Authoritative documented vendor read API |
| Firmware version | Unknown | Unsupported | Documented non-mutating query |
| Lifetime page count | Unknown | Unsupported | Documented non-resetting counter query |
| Roller count | Unknown | Unsupported | Documented non-resetting counter query |
| Consumables | Unknown | Unsupported | Documented read-only state model |
| Device health | Unknown | Unsupported | Documented status/error query |
| Error state | Unknown | Unsupported | Stable vendor error-code contract |
| Maintenance counters | Unknown | Unsupported | Documented non-resetting counter query |

Unknown values never become zero, healthy, available, or supported.

## Windows probe

Run:

```powershell
dotnet run -c Release --project .\tools\Atlas.Edge.ScannerProbe\Atlas.Edge.ScannerProbe.csproj
```

The probe reports detected installations, versions, paths, architecture,
candidate count, provider installation status, and the metadata capability
matrix. It does not invoke any candidate. Capture the output on Jeremy's Windows
workstation to establish the installed PaperStream, Ricoh, and PFU inventory.

## Recommended implementation path

1. Run the safe catalog on the target workstation and retain a reviewed,
   sanitized component inventory.
2. Ask Ricoh/PFU for current SDK documentation, redistribution terms, licensing,
   supported fi-8170 metadata calls, thread/process requirements, and explicit
   confirmation that the desired calls do not acquire images or mutate state.
3. Prefer a vendor-supported metadata-only API. Do not use undocumented exports,
   private named pipes, internal configuration formats, or UI automation.
4. Build an isolated adapter behind `IVendorMetadataProvider`, allowlist exact
   calls, apply timeouts, hash identifiers, and preserve Unknown values.
5. Validate with a test scanner and vendor support before runtime registration.

Until those steps are complete, vendor metadata stays unsupported and the
existing WIA/PnP discovery behavior remains unchanged.
