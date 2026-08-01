# Atlas Edge Windows Service Foundation

Atlas Edge uses the .NET Generic Host and the Windows Service lifetime. The same
runtime worker, enrollment, queue, telemetry, scanner discovery, scanner health,
connector, evidence, and transport registrations run whether the executable is
started interactively or by the Windows Service Control Manager.

## Lifecycle and diagnostics

The service records these in-memory lifecycle phases:

- Startup
- Running
- Stopping
- Stopped

A local diagnostic heartbeat updates independently of the Atlas telemetry
heartbeat. It is not queued, persisted, or transmitted. Read-only diagnostics
include the runtime version, build number, install path, uptime, last service
heartbeat, last scanner discovery, and last scanner health update.

Lifecycle messages use stable Event IDs and flow through the Windows Event Log
provider when the process runs as a Windows Service. The future installer must
register the `Atlas Edge Runtime` Event Log source with the required permissions.
The runtime does not attempt privileged Event Log source creation.

## Local configuration placeholder

The `WindowsService:LocalConfiguration` section is intentionally inert. No file
is read, watched, written, uploaded, or synchronized in this checkpoint.

## Installation boundary

The Windows Service host is ready for Service Control Manager hosting, graceful
stop, and process restart. A future installer checkpoint must install the
published executable, configure automatic startup at boot, register the Event
Log source, set recovery policy, and apply service-account permissions. This
checkpoint does not install or modify a Windows service.
