# Real Scanner Discovery Foundation

The normative design, privacy boundary, identity rules, configuration, and event contract are documented in [Scanner Discovery](105-scanner-discovery.md). This checkpoint adds periodic discovery, deterministic identities and inventory fingerprints, provider timeouts, local queue coalescing, and the safe Windows probe.

Atlas Platform currently accepts only `agent.heartbeat`. `scanner.inventory` remains local, is not persisted durably, and is never offered to HTTP transport.
