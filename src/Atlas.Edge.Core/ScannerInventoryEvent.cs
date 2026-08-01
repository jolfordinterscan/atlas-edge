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
    IReadOnlyList<string> DiscoveryWarnings)
{
    public string? SerialSource { get; init; }
    public string? HardwareId { get; init; }
    public string? DriverProvider { get; init; }
    public string? UsbVendorId { get; init; }
    public string? UsbProductId { get; init; }
    public string? ContainerId { get; init; }
    public string? LocationPathHash { get; init; }
    public string? FriendlyName { get; init; }
    public string? DeviceInstanceIdHash { get; init; }
}

public sealed record ScannerInventoryEnqueueResult(string ReceiptId, bool WasQueued);
