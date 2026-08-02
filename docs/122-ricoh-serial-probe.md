# RICOH Serial Probe

## Scope

`Atlas.Edge.RicohProbe` is an isolated, Windows-only, explicit opt-in proof of concept for reading the serial number of the validated RICOH/FUJITSU fi-8170 through RICOH Scanner Control SDK V2.3. It implements the call-flow and safety conclusions in [121-ricoh-sdk-call-flow.md](121-ricoh-sdk-call-flow.md).

The probe is not part of Atlas Edge Runtime. It does not change WIA discovery, dependency injection, scanner inventory, queueing, transport, or Mission Control. It does not poll, persist, or transmit serial numbers.

The probe supports:

- `--check`: passive OS, architecture, x64 COM registration, and SDK-enabled-build inspection. It does not instantiate the ActiveX control.
- `--read-serial`: the only public mode that starts the supervised ActiveX worker and opens the scanner.

No argument performs no scanner operation and returns `ricoh_probe_explicit_mode_required`. An independent `--list-sources` operation is deliberately unavailable because the SDK source methods require creating the ActiveX host; source enumeration occurs only inside an explicit `--read-serial` session.

## Why OpenScanner is used

The official C# sample uses:

```text
OpenScanner(HWND)
GetSerialNumber(HWND)
CloseScanner(HWND)
```

The probe follows that sequence. `OpenScanner2` is absent from the implementation and abstraction. The RICOH reference manual says `OpenScanner2` keeps the driver open, assumes control of the scanner, and prevents other applications from using it until close. It is forbidden for this proof of concept.

The probe also exposes no image, acquisition, scan, reset, firmware, EEPROM, counter-reset, or configuration method.

## Project and SDK reference strategy

The project uses conditional local references (Strategy A):

- Normal builds target `net8.0`, compile only SDK-free orchestration and the no-op host, and require no Windows desktop or proprietary SDK.
- SDK-enabled builds target `net8.0-windows`, `win-x64`, set `PlatformTarget=x64`, enable WinForms, and compile `WindowsRicohScannerControlHost.Sdk.cs`.
- `EnableRicohSdk=true` and `RicohSdkRoot` are mandatory for an SDK-enabled build.
- The build validates the x64 `AxInterop.FiScnLib.dll`, `Interop.FiScnLib.dll`, and official sample `FormScan.resx` paths beneath `RicohSdkRoot`.
- The ignored local interop assemblies may be copied by MSBuild only into ignored local `bin/obj` output for this proof of concept. Those outputs must not be packaged, published as CI artifacts, staged, committed, or redistributed.
- The sample ActiveX state is read from the ignored official `FormScan.resx` at runtime. Its proprietary serialized value is not copied into Atlas source or embedded as a new repository resource.
- Before the trusted local resource reader deserializes ActiveX state, the complete V2.3L70 sample resource must match SHA-256 `2e1f69bd52dc91d3e79692eef83782643821d57ae9690ac4d3c04fcac46f750c`. A modified or different resource is rejected as `ricoh_activex_creation_failed`.

No absolute development-machine SDK path is stored in source. `RicohSdkRoot` is recorded only in the locally built probe assembly so it can locate the official sample state during that local run.

This approach is for controlled development validation only. It is not a production packaging decision or a redistribution approval.

## ActiveX host

The SDK-enabled host uses the official managed wrapper type:

```text
AxFiScnLib.AxFiScn
```

It creates:

- one dedicated foreground STA thread;
- one WinForms message loop;
- one borderless, taskbar-hidden, transparent, off-screen host form;
- one `AxFiScn` control initialized through `ISupportInitialize`;
- the official `axFiScn1.OcxState` loaded from the local sample `FormScan.resx`; and
- one containing-form HWND passed to every SDK call.

The host disposes the ActiveX control with the form after the operation completes. The scanner is closed before host disposal when open succeeded.

Hidden-host operation remains unverified until the SDK-enabled executable runs on Windows. If ActiveX creation or the hidden form fails, the probe returns a stable sanitized code and must not fall back to visible UI.

## Supported target

The proof of concept accepts only this validated target context:

| Field | Allowed value |
|---|---|
| Manufacturer | `FUJITSU` or `RICOH` |
| Model | `fi-8170` |
| USB VID | `04C5` |
| USB PID | `15FF` |

Missing or different context returns `ricoh_source_unsupported` before the ActiveX host is created. InoTec, SCAMAX, Canon, other fi models, and unknown targets are excluded.

The context is an eligibility check. It does not prove that an SDK source belongs to one physical scanner when identical fi-8170 units are connected.

## Source selection

Inside `--read-serial`, the SDK session calls:

1. `GetSourceCount()`
2. `GetSourceName(index)` for at most 64 sources
3. conservative source resolution
4. `SelectSourceName(exactName)`

The probe never calls the interactive `SelectSource(HWND)` UI.

Without `--source-name`, automatic selection succeeds only when exactly one sanitized SDK source contains normalized `fi8170`. Zero matches return `ricoh_source_not_found`; multiple matches return `ricoh_source_ambiguous`.

With `--source-name`, matching is exact and case-sensitive. The exact source must also identify an fi-8170. A missing exact source returns `ricoh_source_not_found`; an exact unrelated source returns `ricoh_source_unsupported`.

Source names are trimmed, stripped of control characters, bounded to 128 characters, and included in the explicit local JSON. They are not logged elsewhere or transmitted.

## Exact serial call sequence

After preflight, target validation, and machine-wide gate acquisition:

```text
GetSourceCount
GetSourceName (bounded enumeration)
SelectSourceName
OpenScanner(HWND)
GetSerialNumber(HWND) only when open returned RC_SUCCESS
CloseScanner(HWND) in finally after every successful open
Dispose ActiveX host
Exit worker
```

`ErrorCode` is captured immediately after source selection, open, serial read, and close where applicable. No automatic retry or second open occurs. Close failure ends the session with `scannerClosed=false`.

## Session gate

The worker attempts to acquire this named machine-wide mutex without waiting:

```text
Global\InterScan.AtlasEdge.RicohSdk
```

If another Atlas probe holds it, the result is `ricoh_probe_session_active`. The probe does not wait, open, or retry.

This gate serializes Atlas probe sessions only. It does not coordinate with PaperStream Capture or other vendor applications; SDK busy/open results remain authoritative.

## Timeout and process isolation

`--read-serial` is a supervisor operation. It starts one child copy of the probe with an internal worker flag, redirects its output, and permits at most 15 seconds. On timeout, the parent terminates the worker process tree and emits `ricoh_probe_timeout`.

The worker also receives a 15-second cancellation token, but synchronous COM calls may not observe cancellation. The parent process boundary is therefore the enforced watchdog.

Forced termination may leave the TWAIN driver or scanner locked. After any timeout, Windows validation must confirm that PaperStream Capture and the scanner still operate; a reboot may be required. The probe never retries automatically and never opens again after close failure.

## Serial validation and privacy

A returned serial is accepted only when it is:

- non-null and non-empty after trimming;
- at most 128 characters;
- free of control and surrogate characters;
- not `Unknown` or `N/A`;
- not all zeroes;
- not a USB, VID/PID, Windows instance, or topology-style identifier;
- not equal to VID, PID, combined VID/PID, or model name.

Successful explicit local output contains the exact validated serial because obtaining it is the purpose of `--read-serial`. The result also contains a masked form exposing only the final four characters. Ordinary diagnostics contain only stable codes and never raw COM exception text or a serial.

The probe does not write logs, files, registry values, configuration, scanner inventory, or network data.

## Result and error mapping

Every invocation writes one JSON object using schema `1.0`. Unknown or failed values remain `null`, false, zero only where a returned SDK code is actually zero, or `Unknown` for the runtime version. The probe never fabricates a serial or successful close.

Stable diagnostics include:

- `ricoh_probe_explicit_mode_required`
- `ricoh_probe_not_windows`
- `ricoh_probe_not_x64`
- `ricoh_sdk_unavailable`
- `ricoh_activex_creation_failed`
- `ricoh_hidden_host_failed`
- `ricoh_source_not_found`
- `ricoh_source_ambiguous`
- `ricoh_source_unsupported`
- `ricoh_source_selection_failed`
- `ricoh_open_failed`
- `ricoh_scanner_busy`
- `ricoh_serial_empty`
- `ricoh_serial_invalid`
- `ricoh_serial_read_failed`
- `ricoh_close_failed`
- `ricoh_probe_timeout`
- `ricoh_probe_session_active`
- `ricoh_probe_unhandled_failure`

SDK return `-3` and `EC_ERROR_MAX_CONNECTIONS` (`0x2B`) map to scanner busy. Other open failures remain `ricoh_open_failed` because `EC_ERROR_OPEN_DS` has several possible meanings and must not be mislabeled.

## Build commands

Normal repository build, without proprietary references:

```powershell
dotnet build -c Release .\Atlas.Edge.sln
```

SDK-enabled local Windows x64 build:

```powershell
$RicohSdkRoot = "C:\LocalSdk\FiScnSDK23"

dotnet build -c Release `
  .\tools\Atlas.Edge.RicohProbe\Atlas.Edge.RicohProbe.csproj `
  -p:EnableRicohSdk=true `
  -p:RicohSdkRoot="$RicohSdkRoot"
```

The root must contain the official `Sample\ScanTest\VCS 2017\x64\bin\Release` interop assemblies and `Sample\ScanTest\VCS 2017\FormScan.resx`. Do not point the command at a copied or repackaged SDK directory.

## Windows validation commands

Set the local SDK root once:

```powershell
$Project = ".\tools\Atlas.Edge.RicohProbe\Atlas.Edge.RicohProbe.csproj"
$RicohSdkRoot = "C:\LocalSdk\FiScnSDK23"
```

Passive check—the control is not instantiated:

```powershell
dotnet run -c Release --project $Project `
  -p:EnableRicohSdk=true `
  -p:RicohSdkRoot="$RicohSdkRoot" `
  -- --check
```

Independent source listing is intentionally unavailable. This command verifies that it fails without creating the ActiveX host:

```powershell
dotnet run -c Release --project $Project `
  -p:EnableRicohSdk=true `
  -p:RicohSdkRoot="$RicohSdkRoot" `
  -- --list-sources
```

Serial read with automatic unique fi-8170 source selection:

```powershell
dotnet run -c Release --project $Project `
  -p:EnableRicohSdk=true `
  -p:RicohSdkRoot="$RicohSdkRoot" `
  -- --read-serial `
  --manufacturer "FUJITSU" `
  --model "fi-8170" `
  --usb-vid "04C5" `
  --usb-pid "15FF"
```

If multiple fi-8170 SDK sources are returned, rerun only after identifying the exact intended SDK source through an approved local process:

```powershell
dotnet run -c Release --project $Project `
  -p:EnableRicohSdk=true `
  -p:RicohSdkRoot="$RicohSdkRoot" `
  -- --read-serial `
  --source-name "<exact SDK source name>" `
  --manufacturer "FUJITSU" `
  --model "fi-8170" `
  --usb-vid "04C5" `
  --usb-pid "15FF"
```

## Windows validation plan

1. Confirm PaperStream IP and the official x64 RICOH Scanner Control Runtime were installed through approved installers.
2. Close PaperStream Capture for the first run.
3. Run `--check` and confirm x64 runtime registration without scanner activity.
4. Run one `--read-serial` session and record only its JSON result and timing.
5. Compare the serial to an authoritative hardware label or approved RICOH/PFU utility.
6. Confirm `scannerClosed=true` and immediately scan through the normal application.
7. Test disconnected, busy, no-source, ambiguous-source, and exact-source cases.
8. Test while PaperStream Capture is idle and actively scanning. Atlas must fail without interrupting the job.
9. Force a watchdog timeout in a controlled test, then verify driver and scanner recovery.
10. Do not enable runtime polling or inventory integration based solely on one successful read.

## Redistribution limitation

The SDK, runtime, OCX, license, manuals, samples, interop assemblies, and ActiveX state remain proprietary and local-only. This project neither grants nor establishes redistribution permission. The SDK-enabled build and its copied local output must not be committed, packaged, uploaded, or released. Production packaging requires InterScan legal review and RICOH/PFU confirmation as described in the call-flow study.

## Known limitations

- The SDK-enabled project and real fi-8170 path are unverified on Windows.
- Hidden ActiveX hosting and .NET 8 compatibility with the supplied interop assemblies remain unverified.
- SDK source names may not uniquely distinguish identical scanners.
- `OpenScanner` interference and lock duration remain unknown.
- Process termination after timeout may leave vendor driver state requiring recovery.
- The exact serial format and method-level fi-8170 support remain unverified.
- No serial is persisted, merged into WIA inventory, transmitted, or displayed in Mission Control.
