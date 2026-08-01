# Windows Scanner Test Guide

Use a Windows 10 or Windows 11 workstation with the Ricoh scanner attached and its normal production driver installed. These steps enumerate metadata only. They do not initiate scanning or modify the scanner.

## Build and test

```powershell
git clone https://github.com/jolfordinterscan/atlas-edge.git
Set-Location .\atlas-edge
# For an existing working copy, use: git pull --ff-only
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

Do not use `git pull` when the Windows test checkout contains local changes.

## Run the safe probe

```powershell
dotnet run -c Release --project .\tools\Atlas.Edge.ScannerProbe\Atlas.Edge.ScannerProbe.csproj
```

Expected output reports WIA availability and a scanner count. Each scanner displays only manufacturer/model values actually reported by WIA, a masked serial, normalized connection/status/capabilities, and a hashed stable scanner ID. `Unknown` is correct when WIA does not expose a value. Save sanitized output without adding raw WIA property dumps.

For Jeremy's Ricoh, verify the model matches Windows Devices, rerun the probe and confirm the scanner ID is stable, disconnect the USB cable and confirm the next result removes or no longer enumerates it, then reconnect and confirm the same ID returns. Do not run another capture application simultaneously for the first proof.

## Run Atlas Edge locally without transport

```powershell
$env:ATLAS_EDGE_AtlasEdge__EnvironmentName = 'Development'
$env:ATLAS_EDGE_AtlasEdge__TransportMode = 'Null'
$env:ATLAS_EDGE_AtlasEdge__ScannerDiscoveryEnabled = 'true'
$env:ATLAS_EDGE_AtlasEdge__ScannerDiscoveryProvider = 'Platform'
$env:ATLAS_EDGE_AtlasEdge__ScannerDiscoveryProviders__0 = 'Wia'
$env:ATLAS_EDGE_AtlasEdge__ScannerInventoryPublishMode = 'QueueOnly'
$env:ATLAS_EDGE_AtlasEdge__ScannerDiscoveryStartupDelaySeconds = '0'
$env:ATLAS_EDGE_AtlasEdge__ScannerDiscoveryIntervalSeconds = '30'
dotnet run -c Release --project .\src\Atlas.Edge.Runtime\Atlas.Edge.Runtime.csproj
```

Stop gracefully with `Ctrl+C`. To capture sanitized console logs:

```powershell
dotnet run -c Release --project .\src\Atlas.Edge.Runtime\Atlas.Edge.Runtime.csproj *>&1 |
  Tee-Object -FilePath .\atlas-edge-scanner-discovery.log
```

Confirm logs show discovery cycles, WIA count, changed/unchanged inventory behavior, continuing heartbeats, and graceful shutdown. Confirm they do not contain a full serial, device path, token, enrollment code, document name, or user name.

## Troubleshooting

- `Provider available: False`: verify the Windows Image Acquisition service is running and the device appears in Windows Settings/Device Manager.
- Zero scanners: verify the installed device exposes a WIA scanner source; some Ricoh packages expose only TWAIN or vendor interfaces.
- `Unknown` fields: retain them as unknown; do not infer from product literature.
- Timeout: restart the WIA service only under local administrator policy, then rerun. Atlas Edge itself never restarts services.

Ricoh compatibility is unverified until this procedure is completed on the target hardware. Record Windows version, driver package/version, connection type, sanitized probe output, repeated-ID result, runtime logs, and graceful-stop result.
