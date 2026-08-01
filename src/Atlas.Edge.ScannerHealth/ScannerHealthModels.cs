using System.Collections.Immutable;
using Atlas.Edge.ScannerDiscovery;

namespace Atlas.Edge.ScannerHealth;

public enum ScannerDriverHealthStatus
{
    Unknown,
    Ready,
    Degraded,
    Error
}

public sealed record ScannerConsumableHealth(
    string Name,
    decimal? RemainingPercent,
    string? Status);

public sealed record ScannerUsbStability(
    long? DisconnectCount,
    DateTimeOffset? LastDisconnectUtc);

public sealed record ScannerHealthScore(
    int? Mechanical,
    int? Reliability,
    int? Performance,
    int? Connectivity,
    int? Overall);

public sealed record ScannerHealthReading(
    string SourceId,
    ScannerProtocol Protocol,
    string Manufacturer,
    string Model,
    string? SerialNumber,
    long? LifetimePages,
    long? DailyPages,
    decimal? RollerLifePercent,
    decimal? PadLifePercent,
    ImmutableArray<ScannerConsumableHealth> Consumables,
    bool ConsumablesKnown,
    ImmutableDictionary<string, long> MaintenanceCounters,
    bool MaintenanceCountersKnown,
    string? FirmwareVersion,
    decimal? ScanSpeedPagesPerMinute,
    decimal? RatedScanSpeedPagesPerMinute,
    long? JamCount,
    long? DoubleFeedCount,
    long? TransportErrorCount,
    ScannerOnlineStatus OnlineStatus,
    ScannerDriverHealthStatus DriverStatus,
    ScannerUsbStability? UsbStability,
    TimeSpan? DeviceUptime);

public sealed record ScannerHealthSnapshot(
    string ScannerId,
    DateTimeOffset CapturedAtUtc,
    string Manufacturer,
    string Model,
    string? SerialNumber,
    long? LifetimePages,
    long? DailyPages,
    decimal? RollerLifePercent,
    decimal? PadLifePercent,
    ImmutableArray<ScannerConsumableHealth> Consumables,
    bool ConsumablesKnown,
    ImmutableDictionary<string, long> MaintenanceCounters,
    bool MaintenanceCountersKnown,
    string? FirmwareVersion,
    decimal? ScanSpeedPagesPerMinute,
    decimal? RatedScanSpeedPagesPerMinute,
    long? JamCount,
    long? DoubleFeedCount,
    long? TransportErrorCount,
    ScannerOnlineStatus OnlineStatus,
    ScannerDriverHealthStatus DriverStatus,
    ScannerUsbStability? UsbStability,
    TimeSpan? DeviceUptime,
    ImmutableArray<ScannerProtocol> Protocols,
    ScannerHealthScore Score);

public sealed record ScannerHealthProviderDiagnostic(
    ScannerProtocol Protocol,
    bool IsAvailable,
    int ScannerCount,
    string? ErrorCode);

public sealed record ScannerHealthCollectionSnapshot(
    DateTimeOffset CapturedAtUtc,
    ImmutableArray<ScannerHealthSnapshot> Scanners,
    ImmutableArray<ScannerHealthProviderDiagnostic> Diagnostics);

public sealed record ScannerHealthProviderResult(
    ScannerProtocol Protocol,
    bool IsAvailable,
    ImmutableArray<ScannerHealthReading> Readings,
    string? ErrorCode)
{
    public static ScannerHealthProviderResult Available(
        ScannerProtocol protocol,
        IEnumerable<ScannerHealthReading> readings) =>
        new(protocol, true, readings.ToImmutableArray(), null);

    public static ScannerHealthProviderResult Unavailable(ScannerProtocol protocol, string errorCode) =>
        new(protocol, false, ImmutableArray<ScannerHealthReading>.Empty, errorCode);

    public static ScannerHealthProviderResult Failed(ScannerProtocol protocol, string errorCode) =>
        new(protocol, true, ImmutableArray<ScannerHealthReading>.Empty, errorCode);
}
