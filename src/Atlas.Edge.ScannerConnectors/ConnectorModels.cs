using System.Collections.Immutable;

namespace Atlas.Edge.ScannerConnectors;

public enum ConnectorResultState
{
    Known,
    Unknown,
    Unsupported,
    Unavailable,
    Failed
}

public enum ConnectorCapability
{
    Discovery,
    Identity,
    Capabilities,
    Firmware,
    Counters,
    Health,
    CurrentStatus,
    Diagnostics,
    LogReferences
}

public static class ConnectorErrorCodes
{
    public const string DataUnknown = "data_unknown";
    public const string CapabilityUnsupported = "capability_unsupported";
    public const string ConnectorUnavailable = "connector_unavailable";
    public const string AvailabilityCheckFailed = "availability_check_failed";
    public const string DiscoveryFailed = "discovery_failed";
    public const string ReadFailed = "read_failed";
    public const string InvalidResult = "invalid_result";
    public const string TargetNotFound = "target_not_found";

    internal static string Normalize(string? errorCode, string fallback)
    {
        var candidate = errorCode?.Trim();
        return !string.IsNullOrWhiteSpace(candidate) && candidate.All(IsStableCharacter)
            ? candidate
            : fallback;
    }

    private static bool IsStableCharacter(char value) =>
        char.IsAsciiLetterLower(value) || char.IsAsciiDigit(value) || value == '_';
}

public sealed record ConnectorValue<T>
{
    private ConnectorValue(ConnectorResultState state, T? value, string? errorCode)
    {
        State = state;
        Value = value;
        ErrorCode = errorCode;
    }

    public ConnectorResultState State { get; }

    public T? Value { get; }

    public string? ErrorCode { get; }

    public static ConnectorValue<T> Known(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new ConnectorValue<T>(ConnectorResultState.Known, value, null);
    }

    public static ConnectorValue<T> Unknown(string? errorCode = null) =>
        new(
            ConnectorResultState.Unknown,
            default,
            ConnectorErrorCodes.Normalize(errorCode, ConnectorErrorCodes.DataUnknown));

    public static ConnectorValue<T> Unsupported() =>
        new(ConnectorResultState.Unsupported, default, ConnectorErrorCodes.CapabilityUnsupported);

    public static ConnectorValue<T> Unavailable(string? errorCode = null) =>
        new(
            ConnectorResultState.Unavailable,
            default,
            ConnectorErrorCodes.Normalize(errorCode, ConnectorErrorCodes.ConnectorUnavailable));

    public static ConnectorValue<T> Failed(string? errorCode = null) =>
        new(
            ConnectorResultState.Failed,
            default,
            ConnectorErrorCodes.Normalize(errorCode, ConnectorErrorCodes.ReadFailed));
}

public sealed record ConnectorDescriptor(
    string ConnectorId,
    string DisplayName,
    string Protocol,
    string? SupportedManufacturer,
    bool DevelopmentOnly,
    ImmutableArray<ConnectorCapability> Capabilities);

public sealed record ConnectorAvailability(
    ConnectorResultState State,
    string? ErrorCode)
{
    public static ConnectorAvailability Available() => new(ConnectorResultState.Known, null);

    public static ConnectorAvailability Unknown(string? errorCode = null) =>
        new(
            ConnectorResultState.Unknown,
            ConnectorErrorCodes.Normalize(errorCode, ConnectorErrorCodes.DataUnknown));

    public static ConnectorAvailability Unsupported() =>
        new(ConnectorResultState.Unsupported, ConnectorErrorCodes.CapabilityUnsupported);

    public static ConnectorAvailability Unavailable(string? errorCode = null) =>
        new(
            ConnectorResultState.Unavailable,
            ConnectorErrorCodes.Normalize(errorCode, ConnectorErrorCodes.ConnectorUnavailable));

    public static ConnectorAvailability Failed(string? errorCode = null) =>
        new(
            ConnectorResultState.Failed,
            ConnectorErrorCodes.Normalize(errorCode, ConnectorErrorCodes.AvailabilityCheckFailed));
}

public sealed record ScannerConnectionTarget(
    string TargetId,
    string ConnectorId);

public sealed record ScannerIdentity(
    ConnectorValue<string> Manufacturer,
    ConnectorValue<string> Model,
    ConnectorValue<string> SerialNumber,
    ConnectorValue<string> Interface,
    ConnectorValue<string> DriverName,
    ConnectorValue<string> DriverVersion);

public sealed record ScannerCapabilities(
    ConnectorValue<bool> Duplex,
    ConnectorValue<bool> Color,
    ConnectorValue<bool> Feeder,
    ImmutableArray<string> ExposedCapabilities);

public sealed record ScannerCounters(
    ConnectorValue<long> LifetimePages,
    ConnectorValue<long> DailyPages,
    ConnectorValue<long> JamCount,
    ConnectorValue<long> DoubleFeedCount,
    ConnectorValue<long> TransportErrorCount,
    ConnectorValue<ImmutableDictionary<string, long>> MaintenanceCounters);

public sealed record ScannerFirmware(ConnectorValue<string> Version);

public enum ConnectorScannerOnlineStatus
{
    Online,
    Offline
}

public enum ConnectorDriverStatus
{
    Ready,
    Degraded,
    Error
}

public sealed record ScannerStatus(
    ConnectorValue<ConnectorScannerOnlineStatus> OnlineStatus,
    ConnectorValue<ConnectorDriverStatus> DriverStatus);

public sealed record ScannerConsumable(
    string Name,
    ConnectorValue<decimal> RemainingPercent,
    ConnectorValue<string> Status);

public sealed record ScannerHealth(
    ConnectorValue<decimal> RollerLifePercent,
    ConnectorValue<decimal> PadLifePercent,
    ConnectorValue<decimal> ScanSpeedPagesPerMinute,
    ConnectorValue<decimal> RatedScanSpeedPagesPerMinute,
    ConnectorValue<long> UsbDisconnectCount,
    ConnectorValue<DateTimeOffset> LastUsbDisconnectUtc,
    ConnectorValue<TimeSpan> DeviceUptime,
    ConnectorValue<ImmutableArray<ScannerConsumable>> Consumables);

public sealed record ScannerDiagnostics(
    ConnectorValue<long> JamCount,
    ConnectorValue<long> DoubleFeedCount,
    ConnectorValue<long> TransportErrorCount,
    ConnectorValue<ImmutableDictionary<string, long>> MaintenanceCounters);

public sealed record ScannerLogReference(
    string ReferenceId,
    string Kind);

public sealed record ConnectorDiagnostic(
    string ConnectorId,
    string Operation,
    ConnectorResultState State,
    string? ErrorCode);

public sealed record ScannerConnectorObservation(
    ConnectorDescriptor Connector,
    ScannerConnectionTarget Target,
    ConnectorValue<ScannerIdentity> Identity,
    ConnectorValue<ScannerCapabilities> Capabilities,
    ConnectorValue<ScannerFirmware> Firmware,
    ConnectorValue<ScannerCounters> Counters,
    ConnectorValue<ScannerHealth> Health,
    ConnectorValue<ScannerStatus> Status,
    ConnectorValue<ScannerDiagnostics> Diagnostics,
    ConnectorValue<ImmutableArray<ScannerLogReference>> LogReferences);

public sealed record ScannerConnectorSnapshot(
    string ScannerId,
    ConnectorValue<ScannerIdentity> Identity,
    ConnectorValue<ScannerCapabilities> Capabilities,
    ConnectorValue<ScannerFirmware> Firmware,
    ConnectorValue<ScannerCounters> Counters,
    ConnectorValue<ScannerHealth> Health,
    ConnectorValue<ScannerStatus> Status,
    ConnectorValue<ScannerDiagnostics> Diagnostics,
    ConnectorValue<ImmutableArray<ScannerLogReference>> LogReferences,
    ImmutableArray<ScannerConnectorObservation> Observations,
    ImmutableArray<ConnectorDescriptor> Provenance);

public sealed record ScannerConnectorCollectionSnapshot(
    DateTimeOffset CapturedAtUtc,
    ImmutableArray<ScannerConnectorSnapshot> Scanners,
    ImmutableArray<ConnectorDiagnostic> Diagnostics);
