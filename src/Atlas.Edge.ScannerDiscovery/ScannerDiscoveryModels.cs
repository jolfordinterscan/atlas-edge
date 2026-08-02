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

    public ScannerMetadata? EnrichedMetadata { get; init; }

    public IReadOnlyList<ScannerMetadataMatchDiagnostic> MetadataDiagnostics { get; init; } = [];
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

    public string? SerialSource { get; init; }

    public string? HardwareId { get; init; }

    public string? DriverProvider { get; init; }

    public string? UsbVendorId { get; init; }

    public string? UsbProductId { get; init; }

    public string? ContainerId { get; init; }

    public string? LocationPathHash { get; init; }

    public string? FriendlyName { get; init; }

    public string? DeviceInstanceIdHash { get; init; }

    public IReadOnlyList<ScannerMetadataMatchDiagnostic> MetadataDiagnostics { get; init; } = [];
}

public sealed record ScannerMetadata(
    string? SerialNumber,
    string? SerialSource,
    string? HardwareId,
    string? DriverName,
    string? DriverProvider,
    string? DriverVersion,
    string? UsbVendorId,
    string? UsbProductId,
    string? ContainerId,
    string? LocationPathHash,
    string? FriendlyName,
    string? DeviceInstanceIdHash)
{
    public string? FirmwareVersion { get; init; }
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
