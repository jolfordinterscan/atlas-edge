# RICOH SDK Source Enumeration Diagnostics

## Scope

This investigation explains how to compare the RICOH Scanner Control SDK V2.3 source list with Windows WIA, Plug and Play, and installed TWAIN sources. It does not open a scanner, read a serial number, acquire images, select a source, or change Atlas Edge Runtime.

The proprietary SDK remains local and ignored under `vendor/ricoh-sdk/`. Nothing from that directory is packaged or redistributed.

## Official enumeration contract

The complete documented non-UI enumeration sequence is:

1. `int GetSourceCount()`
2. `string GetSourceName(int sourceIndex)` for indexes `0..count-1`
3. `int GetSourceSelect()` to obtain the selected source index

`GetSourceCount` must precede the other two calls. A count of `-1` means failure. The manual explicitly states that `ErrorCode` cannot be used for errors from these three methods, so the diagnostic output reports `sdkErrorCode: null` and `sdkErrorCodeAvailable: false` rather than inventing an error.

Sources:

- `vendor/ricoh-sdk/FiScnSDK23/Manual/Manual.pdf`, printed pages 229-234 (`GetSourceCount`, `GetSourceName`, `GetSourceSelect`, and `SelectSourceName`).
- `vendor/ricoh-sdk/FiScnSDK23/Sample/ScanTest/VCS 2017/FormSourceList.cs`, `FormSourceList_Load`, lines 21-40.
- `vendor/ricoh-sdk/FiScnSDK23/Sample/ScanTest/VC 2017/SourceListDlg.cpp`, `CSourceListDlg::OnInitDialog`, lines 49-70.
- `vendor/ricoh-sdk/FiScnSDK23/Sample/ScanTest/VC 2017/fiscn.h`, generated dispatch wrappers `GetSourceCount` (`0xae`), `GetSourceName` (`0xaf`), and `GetSourceSelect` (`0xb0`), lines 238-255.

`GetSourceSelect` returns an integer index, not a source name. `SelectSource(HWND)` is an interactive selection UI and `SelectSourceName(string)` changes the active selection; neither is part of diagnostic enumeration and neither is called by `--list-sources`.

## Official sample versus Atlas

The official C# and C++ source-list dialogs use the same three list calls, in the same order, as Atlas diagnostics. Therefore, with the same x64 SDK runtime, ActiveX registration, process architecture, and Windows/TWAIN registration context, the sample and Atlas are asking the control for the same logical list.

One lifecycle difference remains: the supplied sample reaches its source-list dialog after its main form has opened the scanner, and the manual example on printed page 230 calls `OpenScanner2` before listing. The individual method prerequisites document `GetSourceCount`, not scanner open, as the prerequisite. Sprint 21C deliberately does not open a scanner; Windows validation must establish whether this SDK/runtime version returns a different list before an open. Atlas will not use `OpenScanner2` to test that question because it is exclusive and outside this diagnostic scope.

## Diagnostic mode

Build the local, SDK-enabled x64 probe as documented in [122-ricoh-serial-probe.md](122-ricoh-serial-probe.md), then run:

```powershell
dotnet run -c Release `
  --project .\tools\Atlas.Edge.RicohProbe\Atlas.Edge.RicohProbe.csproj `
  -p:EnableRicohSdk=true `
  -p:RicohSdkRoot="C:\LocalSdk\FiScnSDK23" `
  -- --list-sources --verbose
```

The operation runs in the existing supervised 15-second helper process and uses the existing fail-fast machine-wide session semaphore. It hosts the ActiveX control because the enumeration methods belong to that control, but calls only `GetSourceCount`, bounded `GetSourceName(index)`, and `GetSourceSelect`.

For each SDK source, JSON includes:

- zero-based SDK index;
- sanitized source name;
- whether its index equals `GetSourceSelect()`;
- source type (`TwainDataSource`, because the SDK manual defines these as TWAIN data sources);
- a conservative driver association, only when one unique normalized WIA/TWAIN/PnP match exists; and
- the explicit fact that the enumeration API provides no retrievable SDK error code.

Verbose environment comparison includes sanitized names, manufacturer, driver name/provider/version, and detected TWAIN registration architecture. It does not emit raw PnP instance IDs, hardware IDs, device paths, registry paths, serial numbers, or credentials.

## Comparison sources

| Catalog | Read-only source | Diagnostic purpose |
|---|---|---|
| SDK | `GetSourceCount`, `GetSourceName`, `GetSourceSelect` | The exact source list visible to the RICOH ActiveX control and its current selected index |
| WIA | Existing `WiaScannerSourceCatalog` | Confirms Windows imaging visibility independent of the SDK |
| Windows PnP | Existing `WindowsPnpScannerMetadataCatalog` | Confirms connected scanner-class devices and associated driver metadata |
| TWAIN | Existing `TwainScannerSourceCatalog` | Identifies installed data-source names and whether their registration was found in the x86 or x64 view |

Name association is diagnostic only. It does not establish that two identically named devices are the same physical scanner, and ambiguous equal matches remain unassociated.

## Filtering hypotheses and how to interpret results

### Active SDK selection

`GetSourceSelect` is documented as the index of the selected source in the enumerated list. Neither the manual nor sample states that it filters `GetSourceCount`. If only InoTec is returned and selected, selection explains which returned entry is active; it does not, by itself, explain why fi-8170 is absent.

### PaperStream Scanner Selection Tool

The manual lists an incorrect PaperStream Scanner Selection Tool device selection as one possible cause of `EC_ERROR_OPEN_DS` during `OpenScanner` (`vendor/ricoh-sdk/FiScnSDK23/Manual/Manual.pdf`, printed pages 301-305). The reviewed documentation does not say that the tool filters the source enumeration list. Because list mode never opens, Sprint 21C cannot use an open error to infer this setting.

### TWAIN default source

The selected index may correspond to the current SDK/TWAIN choice. The reviewed material does not document the Windows TWAIN default as a filter on the returned list. Compare the selected SDK entry with all installed TWAIN registrations; do not assume causation from selection alone.

### Process and driver bitness

Bitness is a concrete candidate. The probe is x64, while TWAIN data-source registrations and driver files may be architecture-specific. The verbose output labels installed TWAIN evidence as `X86`, `X64`, or `Unknown` where the existing catalog can determine it.

Interpret the Windows result conservatively:

- fi-8170 present in WIA/PnP but only an x86 PaperStream TWAIN source present: likely x64 source-registration/driver availability mismatch;
- fi-8170 present in WIA/PnP and an x64 PaperStream source present, but absent from SDK: likely SDK/runtime registration or SDK-specific compatibility issue requiring RICOH confirmation;
- no PaperStream TWAIN source in either architecture: install/repair the supported PaperStream IP TWAIN data source before retesting;
- fi-8170 and InoTec both present in SDK: enumeration is functioning; use the selected index only as selection state;
- only InoTec in all catalogs: Windows is not exposing a PaperStream TWAIN source to this environment even though WIA/PnP may see the hardware.

These are diagnostic branches, not conclusions about Jeremy's workstation until its JSON is captured.

## Safety boundaries

`--list-sources` and `--list-sources --verbose` do not call:

- `SelectSource` or `SelectSourceName`;
- `OpenScanner` or `OpenScanner2`;
- `GetSerialNumber` or `CloseScanner`;
- scan, transfer, acquisition, reset, configuration, firmware, or counter APIs.

No source-list result is persisted, queued, transmitted, or integrated into Atlas Edge Runtime.

## Windows evidence still required

Capture the verbose JSON on the workstation with the fi-8170 and InoTec connected, then repeat these passive checks if practical:

1. Record x64 SDK list, selected index, and WIA/PnP/TWAIN comparison.
2. Record the installed PaperStream IP TWAIN source architecture and version.
3. Compare the official sample source-list UI using the same x64 build/runtime only if RICOH permits safe sample execution in the test environment; this repository does not execute it.
4. Record the PaperStream Scanner Selection Tool setting without changing it during the initial capture.
5. Build the isolated x86 probe explicitly with `-p:RicohProbeArchitecture=x86` and compare its source list with x64. Do not change Atlas Edge Runtime architecture or silently substitute x86.

Until those steps run on Windows, the reason only InoTec is enumerated remains **Unknown**. The strongest testable hypothesis is an architecture-specific PaperStream TWAIN registration or SDK/runtime visibility difference, not active selection alone.
