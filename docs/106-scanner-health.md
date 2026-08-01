# Scanner Health Engine

## Scope

Atlas Edge collects read-only health metadata exposed by local WIA, TWAIN, and ISIS sources during runtime startup. It does not acquire images, change settings, issue scanner commands, predict failures, invoke AI, expose inbound endpoints, or transmit scanner health telemetry.

## Normalized snapshot

Each scanner health snapshot can contain:

- Lifetime and daily page counts
- Roller and pad remaining life
- Named consumables and maintenance counters
- Firmware version
- Measured and rated scan speed
- Jam, double-feed, and transport-error counters
- Online and driver status
- USB disconnect observations
- Device uptime

Every metric is nullable or has an explicit unknown state. Empty consumable or maintenance collections have separate known flags, distinguishing “known empty” from “not exposed.” Invalid, negative, out-of-range, or vendor-specific values that cannot be normalized remain unknown.

## Providers

- WIA consumes read-only metadata returned by Windows Image Acquisition source enumeration.
- TWAIN consumes metadata exposed by installed `.ds` files or registered sources.
- ISIS consumes metadata exposed by recognized registered sources.
- Mock returns obvious synthetic Development data for runtime and contract validation.

Provider availability and failures are reported with stable diagnostic codes. Failure in one provider does not block snapshots from other providers.

## Health scores

Health scores range from 0 to 100 and remain unknown when evidence is insufficient:

- Mechanical averages known roller, pad, and consumable remaining-life percentages.
- Reliability normalizes known jam, double-feed, and transport-error counters by lifetime pages, with double feeds and transport errors weighted more heavily.
- Performance compares measured scan speed with an explicitly exposed rated speed.
- Connectivity averages known online and driver observations plus USB disconnect rate only when device uptime provides a time basis.
- Overall averages only known category scores.

The score calculator is a deterministic local heuristic, not AI or prediction. It never substitutes zero for missing evidence.

## Runtime behavior

Health collection runs once at startup and publishes an in-memory `ScannerHealthCollectionSnapshot`. Only aggregate provider counts and stable status codes are logged. No health event is added to the telemetry queue.

## Deferred scope

- Periodic collection and historical trends
- Vendor-specific SDK integrations beyond exposed source metadata
- Health telemetry transmission
- Prediction, recommendation, AI, automated remediation, and ticketing
- Scanner configuration, commands, or remote control
