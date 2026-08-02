# InoTec Metadata Investigation Probe

## Purpose

`Atlas.Edge.InoTecProbe` creates a structured, read-only inventory of Windows interfaces associated with InoTec, Datawin, and SCAMAX software or devices. It is an investigation tool, not a runtime metadata provider. It does not change WIA discovery or transmit its findings.

Run it from the repository root on Windows:

```powershell
dotnet run -c Release --project .\tools\Atlas.Edge.InoTecProbe\Atlas.Edge.InoTecProbe.csproj
```

The output is JSON schema `1.0`. A connected source named `WIA InoTec SCAMAX USB3` is classified as an InoTec WIA component.

## Inspected surfaces

The Windows source composes existing read-only catalogs and fixed, bounded inspection routines for:

- scanner-class WIA source metadata;
- scanner PnP properties, with device instance, hardware, container, and location identifiers hashed;
- registered TWAIN data sources and ISIS drivers;
- InoTec/Datawin/SCAMAX installed programs and Windows services;
- COM server and TypeLib registrations;
- fixed `HKLM` and `HKCU` InoTec, Datawin, and SCAMAX registry roots, in both registry views;
- driver, DLL, OCX, data-source, and executable file versions under detected installation roots;
- local configuration, status, counter, and diagnostic file references under those roots; and
- exported native function names parsed from Portable Executable headers.

Registry values are not emitted: the probe reports value names only. It does not recursively inspect arbitrary registry roots. File traversal is limited to detected installation directories, four levels, 1,024 files, and skips reparse points. Local metadata and log file contents are never opened or returned.

## Opportunity classification

Each interface lists metadata opportunities for:

- serial number;
- firmware version;
- lifetime page count;
- consumables or roller counters;
- scanner health;
- error state; and
- maintenance counters.

`Promising` means a registration name, file name, or exported-function name contains a direct metadata term such as `serial`, `firmware`, `pagecount`, `roller`, `health`, `error`, or `maintenance`. `Possible` means the interface type could expose the field but requires vendor documentation and licensing review. Neither rating proves that the interface is callable, safe, supported, or authorized.

## Safety and privacy boundaries

The probe:

- never opens a scanner or acquisition session;
- never invokes TWAIN transfer, WIA acquisition, ISIS capture, or a scanner command;
- never instantiates COM objects or TypeLibs;
- never loads or executes a discovered DLL, executable, or data source;
- never resets counters, modifies scanner configuration, or writes the registry;
- never shells out to PowerShell or vendor utilities;
- never reads raw diagnostic or log content; and
- never sends findings to Atlas Platform.

Device instance IDs, hardware IDs, container IDs, location paths, WIA source IDs, COM class IDs, TypeLib IDs, and discovered registry locations are normalized to lowercase SHA-256 values before output. Serial numbers, if Windows already provides one through the existing normalized PnP path, are masked.

Installed paths are shown only when they resolve beneath Program Files, Program Files (x86), Windows, or ProgramData. Other paths are replaced by a SHA-256 reference.

## Interpreting results

The inventory identifies interfaces worth reviewing against official InoTec/Datawin documentation. Export discovery is static evidence only; exported functions must not be called until their ABI, read-only behavior, licensing, initialization requirements, and scanner-side effects are documented and approved. A registered COM server or TypeLib is likewise only a candidate.

The probe intentionally does not claim that serial, firmware, page counts, consumables, health, errors, or maintenance counters are available. Unknown data remains unknown.

## Platform behavior

On non-Windows systems the probe returns a valid snapshot with `IsAvailable` set to `false` and no fabricated interfaces. Real connected-device and installed-software results require a Windows workstation with the InoTec/SCAMAX stack installed.
