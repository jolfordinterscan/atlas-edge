# RICOH Scanner Control SDK V2.3 Call Flow

## Scope

This document records a static study of the locally supplied official RICOH Scanner Control SDK V2.3L70. It maps the documented serial-number call flow to a possible future Atlas proof of concept. It does not approve or implement a provider.

No SDK binary or sample executable was loaded, registered, instantiated, or executed during this study. The SDK remains local-only under the ignored `vendor/ricoh-sdk/` directory. Atlas runtime dependency injection, scanner discovery, inventory behavior, and transport are unchanged.

The central safety finding is that `OpenScanner2` is not appropriate for passive background polling: the reference manual says it keeps the driver open, assumes control of the scanner, and prevents other applications from using the scanner until `CloseScanner`. A future serial-only experiment should start with the less intrusive `OpenScanner`, remain explicit and probe-only, and be tested for contention on Windows before any runtime integration.

## Local SDK artifacts reviewed

The following proprietary files were read locally but are not copied into or tracked by Atlas:

- `vendor/ricoh-sdk/FiScnSDK23/README.TXT`, especially lines 40-105, 107-205, and 251-301.
- `vendor/ricoh-sdk/FiScnSDK23/LICENSE.TXT`, especially lines 1-18 and 20-118.
- `vendor/ricoh-sdk/FiScnSDK23/Manual/GS.pdf`, printed pages 1-22 and 65-69.
- `vendor/ricoh-sdk/FiScnSDK23/Manual/Manual.pdf`, printed pages 97, 218-243, 278-282, and 300-306.
- `vendor/ricoh-sdk/FiScnSDK23/Manual/ManualSeparateVolume.pdf`, reviewed for the fi-8170 compatibility tables.
- `vendor/ricoh-sdk/FiScnSDK23/Sample/ScanTest/VCS 2017/FormScan.cs`, especially `FormScan`, `FormScan_Load`, `FormScan_Closed`, `OpenScanner`, `ButtonSerialNo_Click`, `Dispose`, and `Main`.
- `vendor/ricoh-sdk/FiScnSDK23/Sample/ScanTest/VCS 2017/FormScan.resx`, especially the `axFiScn1.OcxState` resource at lines 123 onward.
- `vendor/ricoh-sdk/FiScnSDK23/Sample/ScanTest/VCS 2017/FormSourceList.cs`, especially `FormSourceList_Load` and `ButtonOK_Click`, lines 21-65.
- `vendor/ricoh-sdk/FiScnSDK23/Sample/ScanTest/VCS 2017/FormSourceList.Designer.cs`.
- `vendor/ricoh-sdk/FiScnSDK23/Sample/ScanTest/VCS 2017/ModuleScan.cs`, especially return constants and open-state fields at lines 412-432.
- `vendor/ricoh-sdk/FiScnSDK23/Sample/ScanTest/VCS 2017/fiScanTest.csproj`, especially lines 1-157 and 180-261.
- `vendor/ricoh-sdk/FiScnSDK23/Sample/ScanTest/VCS 2017/app.config` and `AssemblyInfo.cs`.
- `vendor/ricoh-sdk/FiScnSDK23/Sample/ScanTest/VC 2017/fiscn.h`, the generated dispatch wrapper, especially lines 4-27, 42-68, 186-203, 238-269, and 579-597.
- The names and PE architectures of `FiScn.ocx`, `FiScnSDK64/FiScn.ocx`, `AxInterop.FiScnLib.dll`, and `Interop.FiScnLib.dll`. Their code was not loaded or reverse engineered.

The SDK identifies itself as V2.3L70, dated April 2026 (`README.TXT`, lines 1-8). The supported-scanner list includes `fi-8170` (`README.TXT`, lines 57-89). That establishes general product support, but the reviewed separate-volume tables do not explicitly give a method-level `GetSerialNumber` compatibility statement for the fi-8170. Actual serial retrieval therefore remains a Windows validation item.

## ActiveX and COM object model

| Item | Finding | Evidence |
|---|---|---|
| Control | RICOH Scanner Control ActiveX control | `GS.pdf`, printed pages V and 12-16 |
| OCX | `FiScn.ocx`; separate 32-bit and x64 binaries are supplied | `README.TXT`, lines 127-205; local PE metadata identifies x86 and x64 OCX files |
| Managed wrapper type | `AxFiScnLib.AxFiScn` | `FormScan.cs`, lines 849 and 1809 |
| COM interop namespace | `FiScnLib` for the type-library wrapper; `AxFiScnLib` for the AxHost wrapper | `fiScanTest.csproj`, lines 136-149; sample event types in `FormScan.cs` |
| Interop assemblies | `AxInterop.FiScnLib.dll` and `Interop.FiScnLib.dll` | sample `bin/Release` and `x64/bin/Release`; `GS.pdf`, printed pages 68-69 |
| Type-library GUID | `{383DF550-B568-4E66-99C6-8ABBEE951537}`, version 9.19 | `fiScanTest.csproj`, lines 136-149 |
| ActiveX CLSID | `{383DF553-B568-4E66-99C6-8ABBEE951537}` | generated C++ wrapper `fiscn.h`, lines 9-13 |
| ProgID | **Unknown**; not stated in the reviewed source or manuals | No supported conclusion available |
| AxHost persisted state | `axFiScn1.OcxState` is embedded in `FormScan.resx` | `FormScan.resx`, line 123 onward; assignment in `FormScan.cs`, line 10473 |

The C# control is created by WinForms designer code, initialized through `ISupportInitialize`, assigned persisted `AxHost.State`, sized to 48 by 48 pixels, and added to the visible `FormScan` controls collection (`FormScan.cs`, lines 1809, 10468-10495, and 10585-10586). `Application.Run(new FormScan())` supplies a normal WinForms message loop (`FormScan.cs`, lines 16682-16692).

The official material proves a visible form works. It does **not** prove that a hidden form, message-only window, invisible control, or non-interactive Windows service desktop is supported. Every relevant API takes the handle of the window containing the control (`Manual.pdf`, printed pages 225, 228, 240, and 242). A future experiment must create a valid WinForms handle and verify hidden-host behavior on Windows; it must not assume that any non-zero HWND is sufficient.

## C# sample project configuration

The supplied C# project is an old-style WinForms `WinExe`. Its project file declares `.NET Framework v4.0`, while `GS.pdf` says the supplied Visual Basic and C# 2017 samples require .NET Framework 4.6.2 (`fiScanTest.csproj`, lines 1-46; `GS.pdf`, printed page 11 and sample guidance near printed page 14). Atlas should treat 4.6.2 as the documented sample runtime prerequisite and the v4.0 project declaration as legacy project metadata.

The nominal solution platform defaults to `AnyCPU`, but both Debug and Release `AnyCPU` configurations explicitly set `PlatformTarget` to `x86`. Separate Debug and Release x64 configurations set `PlatformTarget` to `x64` and write beneath `x64/bin` (`fiScanTest.csproj`, lines 8-10 and 48-118). `Prefer32Bit` is absent, so it is not explicitly enabled. The sample output contains architecture-specific copies of both interop assemblies.

The project references:

- `FiScnUtildN4x` (a .NET Framework 4.x bitmap conversion helper);
- `System`, `System.Data`, `System.Drawing`, `System.Windows.Forms`, and `System.Xml`;
- COM type library `FiScnLib` through `tlbimp`;
- ActiveX wrapper `AxFiScnLib` through `aximp`; and
- `stdole` as a primary COM reference.

No explicit `Private`/Copy Local metadata is present on the COM references (`fiScanTest.csproj`, lines 119-157). Generated interop assemblies do appear in both supplied sample output directories, but this fact alone does not establish independent redistribution rights.

The x86 and x64 SDK/runtime paths differ. The package contains `FiScn.ocx` and `FiScnSDK64/FiScn.ocx`; `README.TXT`, lines 117-205, describes 32-bit Windows system locations and architecture-specific utility modules. `GS.pdf`, printed pages 7-9 and 65-67, requires the matching runtime and documents separate x64 project configuration. It also warns that Visual Studio designers do not support displaying the 64-bit component when x64 is active (`GS.pdf`, printed page 65).

`FiScn.LIC` is listed as a required installed license file in both the program and Windows system locations (`README.TXT`, lines 127-132 and 182-185). The generated C++ control wrapper exposes an optional `bstrLicKey` parameter for `CreateControl` (`fiscn.h`, lines 22-27), but neither the C# sample project nor reviewed C# source supplies a license key or `.licx` file. The precise design-time/runtime license mechanism beyond installed `FiScn.LIC` is therefore **Unknown**.

The executable entry point has `[STAThread]` and uses `Application.Run` (`FormScan.cs`, lines 16682-16692). The manuals do not separately state an apartment requirement, but the official C# hosting pattern is STA plus a WinForms message loop. A future proof of concept should reproduce that pattern instead of assuming MTA compatibility.

## Scanner selection

The C# sample starts by opening the currently selected scanner in `FormScan_Load` (`FormScan.cs`, lines 10597-10639 and 11041-11084). It offers two selection mechanisms:

1. `SelectSource(HWND)` displays the SDK/driver source-selection UI. On success, the sample closes an already-open scanner and reopens it (`FormScan.cs`, lines 10685-10715).
2. The source-list form calls `GetSourceCount()`, `GetSourceName(index)`, and `GetSourceSelect()`, then passes the selected display string to `SelectSourceName(string)`. It closes and reopens when the selection changes (`FormSourceList.cs`, lines 21-65).

The manual requires `GetSourceCount` before `GetSourceName`, `GetSourceSelect`, or `SelectSourceName`; source indexes begin at zero, and source names are BSTR strings (`Manual.pdf`, printed pages 229-234). The manual's source-list example calls `OpenScanner2` before enumeration (`Manual.pdf`, printed page 230), while the supplied C# source-list form is entered after the main form has already called `OpenScanner`.

Multiple sources can therefore be distinguished by index and exact source display name. The API does not expose a PnP instance ID, serial, VID/PID, or container ID as part of source enumeration. A future Atlas mapping must be conservative:

- start from the already validated WIA/PnP fi-series scanner;
- enumerate SDK source names without showing selection UI;
- require one unique SDK source whose normalized name maps to the validated manufacturer/model;
- reject equal or multiple candidates;
- never select on manufacturer alone;
- record the exact selected source name only inside the short-lived local session; and
- verify on Windows that the real PaperStream source string for the fi-8170 is stable.

The SDK offers no reviewed evidence that makes this mapping cryptographically or hardware-identity strong. If multiple identical fi-series scanners are attached and only display names differ by unstable indexes, Atlas must leave SDK serial enrichment unavailable rather than risk opening the wrong device.

## OpenScanner and OpenScanner2

### Exact signatures

The managed call shape demonstrated by C# and confirmed by the COM dispatch wrapper is:

```text
int OpenScanner(int hWnd)
int OpenScanner2(int hWnd)
```

The underlying documented signatures are `OpenScanner(hWnd As Integer) [= Integer]` and `OpenScanner2(hWnd As Integer) [= Integer]`. The parameter is the handle of the window containing RICOH Scanner Control (`Manual.pdf`, printed pages 240 and 242; `fiscn.h`, lines 49-54 and 186-191).

Both methods return:

| Value | Symbol | Meaning |
|---:|---|---|
| `0` | `RC_SUCCESS` | Normal completion |
| `2` | `RC_NOT_DS_PSIP` | Selected source is not a PaperStream IP driver |
| `-1` | `RC_FAILURE` | Error; inspect `ErrorCode` |
| `-3` | `RC_SEQUENCE_ERROR` | Another method/form is executing |

The supplied C# sample uses `OpenScanner`, not `OpenScanner2`, in `FormScan.OpenScanner()` (`FormScan.cs`, lines 11041-11059). `GS.pdf` recommends `OpenScanner2` in its scanning tutorial (printed page 18), but that recommendation is aimed at scan throughput, not passive metadata collection.

`OpenScanner` acquires scanner information and initializes the SDK. The manual does not say that it retains exclusive control. `OpenScanner2` explicitly keeps the driver open and assumes control until `CloseScanner`; while held, other applications cannot use the scanner (`Manual.pdf`, printed pages 242-243). Both must be paired with `CloseScanner`, and operations are not guaranteed with multiple control instances (`Manual.pdf`, printed pages 240-243).

The duration of a lock is application-controlled: for `OpenScanner2`, it lasts from successful open until `CloseScanner`. The duration and exclusivity of a successful `OpenScanner` call are not explicitly documented. A real Windows contention test is required.

## GetSerialNumber

The exact managed call shape is:

```text
string GetSerialNumber(int hWnd)
```

The COM documentation describes `GetSerialNumber(hWnd As Integer) [= BSTR]`, where `hWnd` is the containing window handle. A non-empty string is the scanner serial number; an empty string means retrieval failed. When empty, `ErrorCode` may provide the reason (`Manual.pdf`, printed page 228; `fiscn.h`, lines 264-269; `FormScan.ButtonSerialNo_Click`, lines 19137-19148).

`ErrorCode` is a read-only `Long`/managed `int` property initialized to `EC_SUCCESS` whenever a method is called. It applies to all methods except `AboutBox` (`Manual.pdf`, printed page 97; `fiscn.h`, lines 589-597).

The method cannot run while another SDK method is executing. The manual lists no target-method prerequisite for `GetSerialNumber`, but the official C# application calls `OpenScanner` during form load before the serial button can be used. Therefore:

- the exact sample flow is create control -> form load -> `OpenScanner(HWND)` -> later `GetSerialNumber(HWND)` -> form close -> `CloseScanner(HWND)`;
- it is **Unknown** whether an unopened control can reliably retrieve serials despite the method's `Target method: N/A` notation;
- the serial appears to be obtained through the selected scanner/TWAIN driver path, but the documentation does not state whether the originating value is scanner firmware, driver cache, or another source;
- the fi-8170 is supported by SDK V2.3L70 generally, but method-level success remains unverified; and
- no maximum length, character set, normalization, or formatting rule for the returned serial was found.

Atlas must trim and bound a future value, preserve the unmodified value only in the credential/privacy-approved scanner record, mask it in normal logs, and treat empty or malformed output as Unknown.

## CloseScanner and cleanup

The exact managed call shape is:

```text
int CloseScanner(int hWnd)
```

Return values are `0` (`RC_SUCCESS`), `-1` (`RC_FAILURE`, inspect `ErrorCode`), and `-3` (`RC_SEQUENCE_ERROR`). The manual requires every `OpenScanner` or `OpenScanner2` call to be paired with `CloseScanner` and recommends issuing open/start/close from the same form (`Manual.pdf`, printed page 225; `fiscn.h`, lines 56-61).

On normal form closure, the C# sample calls `CloseScanner`, then reads `ErrorCode` and displays a message if close returned failure (`FormScan.cs`, lines 10646-10658). `Dispose(bool)` disposes the WinForms component container and then the base form; it does not explicitly call `Marshal.FinalReleaseComObject` or another COM-release API (`FormScan.cs`, lines 940-953). The top-level `Main` catches exceptions and displays the exception message, but it has no independent `finally` cleanup (`FormScan.cs`, lines 16682-16692).

The official sample therefore does not demonstrate exception-safe scanner closure. A future Atlas experiment must improve on it:

1. Create exactly one control on one STA thread with a message loop.
2. Select and validate exactly one target.
3. Track whether open was attempted and whether it succeeded.
4. Call only `GetSerialNumber` after successful open.
5. Call `CloseScanner` in `finally`, on the same STA/host window.
6. Capture `ErrorCode` immediately after any failed SDK call.
7. Dispose the WinForms host/control after close.
8. Do not invent COM-release behavior; validate whether normal AxHost disposal is sufficient before adding explicit release calls.

Whether close and AxHost disposal require pumping additional Windows messages is **Unknown**. The official sample keeps `Application.Run` active throughout, so a future host should keep its message loop alive until close and disposal complete.

## Threading and window-handle requirements

- A real HWND is a documented parameter for open, serial, and close; it is described as the handle of the window containing the control (`Manual.pdf`, printed pages 225, 228, 240, and 242).
- The official C# entry point is `[STAThread]` and runs a WinForms message loop (`FormScan.cs`, lines 16682-16692).
- The ActiveX control is created and used by the same form. The manual recommends same-form sequencing and says multiple control instances are unsupported (`Manual.pdf`, printed pages 225 and 240-243).
- A visible control is demonstrated. Hidden or service-session hosting is not documented.
- A future POC should use one dedicated STA thread, one control, one host window, and one global session gate. It must not call the control from thread-pool or heartbeat threads.

## Error and busy-state mapping

| Condition | Official source/sample behavior | Safe Atlas behavior | Retry and lock assessment |
|---|---|---|---|
| SDK/runtime unavailable | ActiveX construction would fail before SDK return codes are available; top-level sample catches a general exception | Report `Unavailable` with a stable code; never expose raw COM text | Do not retry in-cycle; retry only after installation/configuration change. No known scanner lock if construction never completed. |
| ActiveX creation/state failure | Designer creates `AxFiScnLib.AxFiScn` and applies `OcxState`; sample has only top-level exception UI | Fail closed, dispose partial host, return sanitized diagnostic | No automatic retry. Lock state is Unknown if initialization reached driver code. |
| No scanner connected | Open may return `-1`; `ErrorCode` can be `EC_DEVICE_NOT_FOUND` (`0x0000001D`) or `EC_ERROR_OPEN_DS` (`0x0000001A`) | Return Unknown/Unavailable; do not create serial | A later manual probe may retry after connection changes. Do not poll aggressively. |
| Scanner/driver busy | `EC_ERROR_OPEN_DS` may mean another application owns the driver; `EC_ERROR_MAX_CONNECTIONS` is `0x0000002B` | Stop immediately and leave the capture application undisturbed | At most one delayed manual retry. The failed call may have partially initialized state; perform best-effort documented cleanup. |
| Unsupported source/scanner | Open returns `2` for non-PaperStream IP; `EC_ERROR_NOT_SUPPORTED_DS` is `0x00000029` | Mark Unsupported; do not try another similarly named source blindly | Non-retryable until selection/driver changes. |
| Open failure | Sample reads `ErrorCode`, disables scan functions, and records open false | Capture return and error code; do not call serial | Retry only documented transient codes such as not-ready/busy, with a strict bound. Potential partial lock is Unknown. |
| Empty serial with `EC_SUCCESS` | Sample treats every empty value as error and displays `ErrorCode`; manual says errors *may* be indicated | Return Unknown with `ricoh_serial_empty`; do not fabricate | No immediate retry unless Windows validation establishes a transient case. |
| `GetSerialNumber` error | Empty BSTR; inspect `ErrorCode`; method cannot overlap another method | Return Unknown plus mapped stable code; never log raw serial | Retry only a clearly transient not-ready/busy code, once, after closing the session. |
| Close failure | Sample reads `ErrorCode` and displays it | Record sanitized close failure, dispose host, terminate probe session, and warn that scanner availability is uncertain | Do not reopen or loop. A scanner/driver lock may remain until host exit, driver recovery, or reboot. |

The error meanings above come from `Manual.pdf`, printed pages 301-305. In particular, `EC_ERROR_OPEN_DS` lists disconnected, powered-off, incorrect selection, and another application using the driver as possible causes; `EC_DEVICE_NOT_FOUND` includes disconnected or in-use cases; and `EC_ERROR_MAX_CONNECTIONS` identifies another application using the driver. Return code `-3` allows retry after the currently executing method completes, but Atlas should not use that statement to justify background contention with capture software.

## Scanner-interference risks

The interference risk is material:

- `OpenScanner2` is explicitly exclusive until close and prevents other applications from using the scanner (`Manual.pdf`, printed pages 242-243).
- `OpenScanner` initializes the scanner and selected TWAIN source. Its exact lock duration is not documented.
- Open errors explicitly include the TWAIN driver being used by another application (`Manual.pdf`, printed pages 302-303).
- `GetSerialNumber` cannot execute concurrently with another SDK method (`Manual.pdf`, printed page 228).
- The SDK says operations are not guaranteed with two control instances (`Manual.pdf`, printed pages 240-243).
- The sample opens at application startup and closes only at application exit, which is unsuitable for Atlas background use (`Manual.pdf`, printed page 279; `FormScan.cs`, lines 10597-10658).

There is no evidence that querying serial during an active PaperStream Capture scan is safe. The shortest candidate session is source validation -> `OpenScanner` -> `GetSerialNumber` -> `CloseScanner` in `finally`, but its real lock and latency must be measured. `OpenScanner2` must not be used for the initial metadata POC because its exclusivity is documented.

Initial use should be a manually invoked probe flag only. Production runtime polling is unsafe without vendor confirmation and Windows tests covering PaperStream Capture idle, busy, scan-in-progress, cancellation, crash, and close-failure scenarios. A conservative future timeout is 5 seconds per SDK call and 15 seconds for the entire session, subject to Windows measurement. Because COM calls may not be cancellable, timeout handling should occur within a disposable helper-process boundary; forced termination itself must be tested for driver-lock recovery.

## Atlas integration mapping

| Official SDK concept | Official sample location | Proposed Atlas abstraction |
|---|---|---|
| ActiveX scanner control `AxFiScnLib.AxFiScn` | `FormScan.cs`, lines 849, 1809, and 10468-10495 | `IRicohScannerControlClient` |
| STA WinForms host and HWND | `FormScan.Main`, lines 16682-16692; API manual pages 225/228/240/242 | `IRicohActiveXHost` |
| Source enumeration | `FormSourceList_Load`, lines 21-41 | `ListSources`/`ListSourcesAsync` |
| Exact source selection | `FormSourceList.ButtonOK_Click`, lines 47-64 | `SelectSource` using validated exact source name |
| Scanner open | `FormScan.OpenScanner`, lines 11041-11059 | `Open`/`OpenAsync` returning a bounded result |
| Serial read | `ButtonSerialNo_Click`, lines 19137-19148 | `ReadSerialNumber` |
| SDK error | `axFiScn1.ErrorCode`; manual printed page 97 | `RicohSdkError` with stable mapping |
| Scanner close | `FormScan_Closed`, lines 10646-10658 | `Close` plus session `Dispose` |
| Session serialization | Manual restriction against overlapping methods/control instances | `IRicohSdkSessionGate` |

These names describe a possible design only. None is implemented.

## Required dependencies

A future x64 POC would require, at minimum:

- Windows x64 and an interactive test session;
- the matching RICOH Scanner Control Runtime x64 installed through the official runtime installer;
- registered x64 `FiScn.ocx` and its documented runtime dependencies;
- installed `FiScn.LIC` in the location established by the official installer;
- PaperStream IP (TWAIN) for the target fi-series scanner;
- WinForms/ActiveX hosting support;
- generated `AxInterop.FiScnLib.dll` and `Interop.FiScnLib.dll` references compatible with the installed type library; and
- one STA thread with a WinForms message loop and valid host HWND.

The SDK README also lists Visual C++ 2022 runtime libraries and several helper modules (`README.TXT`, lines 127-205). Which subset a serial-only x64 call actually loads must be measured with the official runtime installed; Atlas must not hand-copy guessed DLL subsets.

## Licensing and redistribution boundary

This section records source language and uncertainty; it is not legal advice or a production distribution approval.

The included agreement permits installing/using the SDK to develop software for fi-series control, modifying sample code into object code, and distributing the developer's software to end users, subject to the agreement (`LICENSE.TXT`, lines 20-47). It prohibits reverse engineering, bypassing technical limitations, unsupported control functions, and distributing or licensing the SDK itself except as permitted (`LICENSE.TXT`, lines 50-99).

`GS.pdf`, printed page 4, distinguishes the development SDK from `RICOH Scanner Control Runtime` and `RICOH Scanner Control Runtime (x64)`, which are required for distributed applications. Printed page 22 instructs developers to distribute only the matching unchanged `SETUP_DISC\FiScnRun` or `SETUP_DISC\FiScnRun64` runtime folder and explicitly says other folders cannot be distributed. The locally extracted tree reviewed here does not establish that it is one of those approved runtime folders.

Therefore:

- Developer SDK files, manuals, samples, and the local archive must not be copied into Atlas, its installer, artifacts, or source control.
- Atlas must not directly package the locally extracted `FiScn.ocx`, `FiScn.LIC`, helper DLLs, or SDK folders based on this study.
- The official x64 runtime installer folder is the documented distribution unit; whether InterScan may redistribute it with Atlas still requires license/counsel review and confirmation that the actual obtained package contains the approved unchanged `FiScnRun64` folder.
- Whether generated `AxInterop.FiScnLib.dll` and `Interop.FiScnLib.dll` may be bundled independently is **Unknown**. Their presence in sample output is technical evidence, not an explicit redistribution grant.
- Required notices, acceptance flow, attribution, installer chaining rules, and update rights are **Unknown** beyond the included agreement and the instruction not to modify the approved runtime folder.
- No production packaging should proceed without InterScan legal approval and, where ambiguity remains, written RICOH/PFU confirmation.

## Recommended proof-of-concept design

The next implementation, if separately approved, should be a narrow extension to the existing scanner probe—not a runtime provider:

1. Add an explicit Windows-only `--ricoh-sdk-serial` flag. The default probe remains passive and never instantiates the SDK.
2. Require x64 and detect the official x64 runtime/type-library registration. Do not load files from `vendor/`.
3. Run the opt-in operation in a short-lived helper process containing one STA thread, one WinForms message loop, one ActiveX control, and one hidden host form only after Windows proves hidden hosting works.
4. Enumerate SDK source names and select only one unambiguous source mapped to the already validated fi-series WIA/PnP target. Abort on ambiguity.
5. Acquire a machine-wide single-session gate so no two Atlas SDK sessions overlap.
6. Use `OpenScanner`, not `OpenScanner2`, for the first experiment because `OpenScanner2` has documented exclusive behavior.
7. On `RC_SUCCESS`, call only `GetSerialNumber`.
8. Read `ErrorCode` immediately on failed open, empty serial, or failed close.
9. Always call `CloseScanner` in `finally` on the same STA/host, then dispose the host and exit the helper.
10. Enforce a proposed 5-second per-call and 15-second session watchdog, refined from real measurements. Do not continue background work after timeout.
11. Mask serials in ordinary output/logs; require an explicit privileged probe-output mode for the full value.
12. Do not call `StartScan`, `Transfer`, `ShowAcquireImage`, source settings, feeder, reset, EEPROM, capability-setting, image, or acquisition APIs.
13. Do not alter WIA discovery, inventory identity, runtime DI, queueing, heartbeat, or transport during the POC.
14. Keep the feature probe-only until idle and active-scan interference tests pass and RICOH confirms the intended metadata use.

## Unknowns requiring Windows validation

- Whether the x64 ActiveX control can be hosted invisibly and without an interactive desktop.
- Whether normal AxHost disposal is sufficient or explicit COM release is required.
- Whether close/dispose needs extra message-pump cycles.
- The exact registered ProgID and installation registry paths on the target workstation.
- The actual PaperStream source display name for the fi-8170 and whether it uniquely maps when identical models are attached.
- Whether `GetSerialNumber` succeeds on the fi-8170 and what exact format/length it returns.
- Whether the serial comes directly from hardware or from driver-maintained metadata.
- Whether `GetSerialNumber` technically works without open; the sample opens first despite the manual listing no target-method dependency.
- Whether `OpenScanner` blocks PaperStream Capture and how long it retains driver resources.
- Exact behavior when PaperStream Capture is idle, busy, or actively scanning.
- Whether timeouts can leave the TWAIN source or scanner locked.
- Whether a Windows Service session can create/use the control; initial validation should use an interactive probe only.
- Which runtime dependencies a serial-only x64 session loads.
- Independent redistribution rights for interop assemblies and the exact approved runtime package.

## Exact next implementation steps

1. Obtain product/security approval for an interactive, probe-only Windows POC and legal approval to use the installed SDK/runtime under the included agreement.
2. Confirm the official x64 runtime is installed using its installer; do not register the local OCX manually.
3. Record the x64 CLSID, type-library version, runtime version, and exact source names from registry/type-library metadata without invoking the control.
4. Create test-only abstractions for ActiveX host, SDK control, clock/watchdog, source selector, and session gate; unit-test all behavior with fakes.
5. Build a Windows x64 helper executable that activates only with `--ricoh-sdk-serial` and exits if runtime, STA, HWND, or source validation fails.
6. Validate hidden-host creation without opening a scanner; if unsupported, stop and seek RICOH guidance rather than displaying unexpected UI.
7. With PaperStream Capture closed, perform one `OpenScanner` -> `GetSerialNumber` -> `CloseScanner` session and record sanitized timings and return/error codes.
8. Confirm the reported serial against an authoritative hardware label or approved device utility.
9. Repeat with PaperStream Capture idle and then actively scanning; Atlas must back off immediately on busy/in-use results and must never interrupt the job.
10. Test no scanner, wrong source, unsupported source, duplicate model, open failure, empty serial, timeout, process crash, and close failure.
11. Verify the scanner remains usable after every test and after forced helper termination.
12. Only after those results, decide whether a production metadata provider is safe. Do not introduce polling by default.
