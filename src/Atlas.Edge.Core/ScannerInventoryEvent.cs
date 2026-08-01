namespace Atlas.Edge.Core;

public sealed record ScannerInventoryEvent(
    string EventId,
    string EventType,
    string SchemaVersion,
    DateTimeOffset ObservedAtUtc,
    string AgentId,
    string WorkstationId,
    string InventoryVersion,
    int ScannerCount,
    IReadOnlyList<ScannerInventoryEntry> Scanners);

public sealed record ScannerInventoryEntry(
    string ScannerId,
    string ProviderId,
    string ProviderName,
    string Manufacturer,
    string Model,
    string? SerialNumber,
    string? DevicePathHash,
    string? DriverName,
    string? DriverVersion,
    string DriverType,
    string ConnectionType,
    string? FirmwareVersion,
    string Status,
    bool IsAvailable,
    IReadOnlyList<string> Capabilities,
    DateTimeOffset FirstObservedUtc,
    DateTimeOffset LastObservedUtc,
    string MetadataConfidence,
    IReadOnlyList<string> DiscoveryWarnings);

public sealed record ScannerInventoryEnqueueResult(string ReceiptId, bool WasQueued);
