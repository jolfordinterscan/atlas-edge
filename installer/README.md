# Atlas Edge Windows installer foundation

This directory defines the InterScan-branded, per-machine Atlas Edge installer.
It uses WiX Toolset v4 because WiX provides native MSI upgrade, service,
registry, repair, uninstall, and Burn bootstrapper behavior without scripts or
custom actions running from writable directories.

## Experience and readiness

The MSI uses this branded WiX flow:

1. Welcome: official InterScan logo, **Atlas Edge**, **Enterprise Scanner
   Intelligence**, publisher, and the approved monitoring description.
2. End User License Agreement: the `DRAFT-0.1` review document requires
   explicit acceptance. It is not approved for production distribution.
3. Telemetry and Privacy Disclosure: the `DRAFT-0.1` review document describes
   operational telemetry and excluded content and requires acknowledgement.
4. Administrator Authorization: the administrator confirms organizational
   authorization. This does not replace Windows elevation.
5. Installation Directory / Readiness: launch conditions require supported 64-bit Windows and an
   elevated per-machine install. The runtime is self-contained, so no shared
   .NET runtime is required. Windows Installer performs disk-cost and existing
   product checks; `MajorUpgrade` detects related versions and blocks downgrade.
6. Progress: standard MSI action text reports runtime installation, service
   registration/automatic startup, Event Log registration, local directory
   creation, recovery configuration, and service startup.
7. Completion: the branded completion page states that Atlas Edge was installed,
   the service and automatic startup were configured, and the local monitoring
   foundation is ready. It explicitly states that enrollment and cloud
   connectivity are not configured.

The custom dialogs use MSI-native controls and properties; there are no
executable UI custom actions. Interactive Next buttons remain disabled until
the applicable acknowledgement is checked. Back navigation preserves the MSI
property state for review during the same installation session.

The setup executable is a thin WiX Burn bootstrapper over the MSI. It shares
the official logo and preserves MSI silent/repair semantics.

## Installed layout

- Runtime: `C:\Program Files\InterScan\Atlas Edge`
- Shared data: `C:\ProgramData\InterScan\Atlas Edge`
  - `configuration`
  - `identity`
  - `diagnostics`

Program Files remains protected by Windows defaults. ProgramData grants full
control only to `SYSTEM` and local administrators; no `Everyone` or `Users`
write grant is added. The service runs as LocalSystem in this foundation.

The installed configuration is local and production-safe: null transport,
platform discovery/health, disabled connector/evidence startup, no enrollment
code, no real Atlas URL, and no mock providers. The `.invalid` HTTPS endpoints
are deliberately non-routable placeholders required by existing validation.

## Service and Event Log

- Service name/display name: `Atlas Edge Runtime`
- Description: `Enterprise scanner intelligence and monitoring for InterScan Atlas`
- Startup: Automatic
- Executable: `[INSTALLFOLDER]Atlas.Edge.Runtime.exe`; Windows Installer writes
  the SCM image path as a quoted path because the installation directory has
  spaces.
- Event Log source: `Application\Atlas Edge Runtime`
- Recovery: restart after first, second, and subsequent failures after 60
  seconds; reset the failure count after one day.

WiX standard `ServiceInstall`, `ServiceControl`, registry, and service-recovery
elements are used. No firewall rule, listener, dynamic plug-in, scanner command,
or custom executable action is installed.

## Upgrade, repair, and uninstall

The MSI UpgradeCode is stable. Product codes change automatically for major
upgrades. Same-version maintenance supports repair. `MajorUpgrade` blocks
downgrades and cleanly replaces older packages.

Runtime binaries, service registration, and Event Log registration are removed
on uninstall. Configuration, identity, and diagnostics directories are
`Permanent` and retained on upgrade and uninstall. Customer-generated evidence,
diagnostics, or future enrollment identity is never silently removed. An
administrator must deliberately remove retained ProgramData after uninstall.

## Legal-review and privacy boundary

Files under `legal/` are review drafts. Every legal document is marked
**DRAFT FOR REVIEW — NOT APPROVED FOR PRODUCTION USE**. Final EULA, privacy,
warranty, liability, governing-law, export, notice, and related terms require
approval by InterScan legal counsel. The build must not be released while draft
versions remain configured.

Atlas Edge is designed to observe operational health, not document content.
The disclosure lists authorized operational categories and expressly excludes
images, OCR text, document content and metadata, unrelated personal content,
passwords, tokens, enrollment codes, and private keys.

Atlas Edge may support digitally signed software updates in a future release.
Automatic update behavior is not enabled by this installer foundation. Update
policy, consent, maintenance windows, rollback, signing, and enterprise
administration remain future work.

## Acceptance properties and local record

The public properties below are MSI-secure and contain only the non-secret value
`1` when acknowledged:

- `ATLAS_ACCEPT_EULA`
- `ATLAS_ACCEPT_TELEMETRY`
- `ATLAS_ADMIN_AUTHORIZED`

Silent and basic-UI installation is rejected with an actionable launch-condition
message unless all three equal `1`. Maintenance of an already installed product
does not require the initial-install properties again.

Initial installation writes this limited operational record beneath
`HKLM\Software\InterScan\Atlas Edge\InstallerAcceptance`:

- Installer version
- EULA document version
- Telemetry disclosure version
- Installer-session acceptance timestamp field
- Installation mode (`interactive` or `silent`)

It stores no username, password, token, enrollment code, scanner identifier, or
document content. The permanent record is retained on uninstall with other
ProgramData identity/configuration records. It is an operational deployment
record, not legal proof beyond whatever InterScan legal counsel ultimately
approves. The timestamp’s UTC behavior must be verified on Windows before pilot
release; the MSI authoring deliberately uses no executable time custom action.

## Branding

The official source asset is `assets/interscan-logo.svg`, copied byte-for-byte
from `amazon-scanner-dashboard/static/images/interscan-logo.svg`. Its checksum
and generated WiX dimensions are documented in `assets/README.md`.

ImageMagick creates build-only 493x58 BMP, 493x312 BMP, and 450x150 PNG assets.
The branded build stops with an actionable error when the source logo is absent,
modified, or ImageMagick is unavailable. No official ICO was found, so no icon
is invented or configured.

## Windows build

Prerequisites: Windows 10/11 or Windows Server 2016+, .NET 8 SDK, PowerShell 7,
ImageMagick (`magick.exe` on PATH), and network access to restore WiX v4 SDK and
extensions.

```powershell
pwsh ./installer/scripts/build-installer.ps1 -Configuration Release -BuildNumber local
```

The script derives `0.6.0` from the current repository version, publishes a
self-contained `win-x64` runtime, replaces development configuration with the
installer-safe configuration, generates the WiX payload, builds MSI and setup,
and writes SHA-256 sidecars plus `manifest.json` beneath
`artifacts/installer/<version>/`. That directory is ignored.

The current macOS development host can validate source, XML, metadata, tests,
and scripts but cannot run WiX packaging or Windows Installer. A Windows build
must execute the command above before artifacts exist.

## Signing

Local builds are unsigned. Production signing uses an externally supplied
Authenticode certificate and timestamp URL; no certificate is stored here:

```powershell
$env:ATLAS_EDGE_SIGN_CERTIFICATE_PATH = 'D:\secure\codesign.pfx'
$env:ATLAS_EDGE_SIGN_CERTIFICATE_PASSWORD = '<secret>'
$env:ATLAS_EDGE_SIGN_TIMESTAMP_URL = 'https://timestamp.example'
pwsh ./installer/scripts/build-installer.ps1 -Configuration Release -Sign
```

The script signs the runtime executable before packaging, then the MSI and setup
executable. Production automation must inject secrets through its secure secret
store and should verify signatures after packaging.

## Operations and logs

Verbose install:

```text
msiexec /i AtlasEdge-0.6.0-win-x64.msi /l*v AtlasEdge-install.log
```

Silent install:

```text
msiexec /i AtlasEdge-0.6.0-win-x64.msi /qn ATLAS_ACCEPT_EULA=1 ATLAS_ACCEPT_TELEMETRY=1 ATLAS_ADMIN_AUTHORIZED=1 /l*v AtlasEdge-install.log
```

Silent rejection test (expected to fail before installation):

```text
msiexec /i AtlasEdge-0.6.0-win-x64.msi /qn /l*v AtlasEdge-rejected.log
```

Repair:

```text
msiexec /fa AtlasEdge-0.6.0-win-x64.msi /qn /l*v AtlasEdge-repair.log
```

Silent uninstall (use the installed ProductCode):

```text
msiexec /x {PRODUCT-CODE} /qn /l*v AtlasEdge-uninstall.log
```

Common Windows Installer exit codes are `0` success, `3010` success/reboot
required, `1602` user cancelled, `1603` fatal error, `1618` another installation
is active, and `1638` another product version is installed. Preserve verbose
logs when escalating failures; installer logs contain no raw scanner identifiers
or credentials by design.
