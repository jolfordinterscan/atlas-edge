# Installer branding assets

`interscan-logo.svg` is an unmodified copy of the official InterScan logo from
`amazon-scanner-dashboard/static/images/interscan-logo.svg`.

- Source dimensions: 450 x 150 pixels (`viewBox="0 0 337.5 112.499997"`)
- SHA-256: `981c99b7d7b4a4985764bbca42b03998ef906fcac1444e0cbc45b7ba52cb7d0d`
- WiX banner output: 493 x 58, 24-bit BMP
- WiX dialog output: 493 x 312, 24-bit BMP
- Bootstrapper logo output: 450 x 150, transparent PNG

The raster files are deterministic build outputs under `artifacts/installer/`
and are not committed. `prepare-brand-assets.ps1` verifies the source checksum
and uses ImageMagick to place the official logo on the required canvases.

No official InterScan application icon was found. An official ICO containing
16, 32, 48, and 256 pixel images is required before an application or ARP icon
can be enabled. Do not substitute or synthesize an icon.
