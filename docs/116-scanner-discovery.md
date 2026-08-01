# Real Scanner Discovery Foundation

The normative design, privacy boundary, identity rules, configuration, and event contract are documented in [Scanner Discovery](105-scanner-discovery.md). This checkpoint adds periodic discovery, deterministic identities and inventory fingerprints, provider timeouts, local queue coalescing, and the safe Windows probe.

Atlas Platform schema `1.0` compatibility is required for inventory transport. `QueueOnly` remains the conservative default. `Transport` sends only changed coalesced inventories over the existing authenticated batch endpoint; the pending slot is not persisted durably.
