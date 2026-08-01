namespace Atlas.Edge.ScannerDiscovery;

public enum ScannerProtocol
{
    Twain,
    Wia,
    Isis,
    Mock
}

public enum ScannerOnlineStatus
{
    Unknown,
    Online,
    Offline
}

public enum ScannerOperationalStatus
{
    Ready,
    Busy,
    Offline,
    Unavailable,
    Error,
    Unknown
}

public enum ScannerCapability
{
    AutomaticDocumentFeeder,
    Flatbed,
    Duplex,
    Color,
    Grayscale,
    BlackAndWhite,
    MultiPage,
    Unknown
}

public enum ScannerConnectionType
{
    Usb,
    Scsi,
    Network,
    Unknown
}

public enum ScannerMetadataConfidence
{
    ProviderStableIdentity,
    SerialIdentity,
    DevicePathIdentity,
    MetadataFallback
}

public sealed record ScannerDriver(
    string Name,
    string? Version,
    string? Provider);

public sealed record AdapterScannerDevice(
    string SourceId,
    ScannerProtocol Protocol,
    string Manufacturer,
    string Model,
    string? SerialNumber,
    string? FirmwareVersion,
    string Interface,
    bool? SupportsDuplex,
    bool? SupportsColor,
    bool? HasFeeder,
    IReadOnlyList<string> Capabilities,
    ScannerDriver Driver,
    ScannerOnlineStatus OnlineStatus)
{
    public string? DevicePath { get; init; }

    public bool HasProviderStableIdentity { get; init; } = true;
}

public sealed record DiscoveredScanner(
    string DiscoveryId,
    string Manufacturer,
    string Model,
    string? SerialNumber,
    string? FirmwareVersion,
    string Interface,
    bool? SupportsDuplex,
    bool? SupportsColor,
    bool? HasFeeder,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<ScannerDriver> Drivers,
    ScannerOnlineStatus OnlineStatus,
    IReadOnlyList<ScannerProtocol> Protocols)
{
    public string ProviderId { get; init; } = "unknown";

    public string ProviderName { get; init; } = "Unknown";

    public string? DevicePathHash { get; init; }

    public ScannerOperationalStatus Status { get; init; } = ScannerOperationalStatus.Unknown;

    public ScannerConnectionType ConnectionType { get; init; } = ScannerConnectionType.Unknown;

    public ScannerMetadataConfidence MetadataConfidence { get; init; } = ScannerMetadataConfidence.MetadataFallback;

    public DateTimeOffset FirstObservedUtc { get; init; }

    public DateTimeOffset LastObservedUtc { get; init; }

    public IReadOnlyList<ScannerCapability> NormalizedCapabilities { get; init; } = [];

    public IReadOnlyList<string> DiscoveryWarnings { get; init; } = [];
}

public sealed record ScannerAdapterDiagnostic(
    ScannerProtocol Protocol,
    bool IsAvailable,
    int DeviceCount,
    string? ErrorCode)
{
    public TimeSpan CollectionDuration { get; init; }

    public DateTimeOffset CollectedAtUtc { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record ScannerDiscoverySnapshot(
    DateTimeOffset DiscoveredAtUtc,
    IReadOnlyList<DiscoveredScanner> Scanners,
    IReadOnlyList<ScannerAdapterDiagnostic> Diagnostics);

public sealed record ScannerAdapterResult(
    ScannerProtocol Protocol,
    bool IsAvailable,
    IReadOnlyList<AdapterScannerDevice> Devices,
    string? ErrorCode)
{
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public static ScannerAdapterResult Available(
        ScannerProtocol protocol,
        IReadOnlyList<AdapterScannerDevice> devices) =>
        new(protocol, true, devices, null);

    public static ScannerAdapterResult Unavailable(ScannerProtocol protocol, string errorCode) =>
        new(protocol, false, Array.Empty<AdapterScannerDevice>(), errorCode);

    public static ScannerAdapterResult Failed(ScannerProtocol protocol, string errorCode) =>
        new(protocol, true, Array.Empty<AdapterScannerDevice>(), errorCode);
}
