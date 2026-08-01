using System.Collections.Immutable;

namespace Atlas.Edge.ScannerEvidence;

public enum EvidenceValueState
{
    Known,
    Unknown,
    Unsupported,
    Unavailable,
    Failed
}

public enum EvidenceSourceQuality
{
    DirectDevice,
    StandardProtocol,
    OperatingSystem,
    VendorUtility,
    NetworkInterface,
    LocalLog,
    UserConfigured
}

public enum EvidenceCapability
{
    Discovery,
    DeviceIdentity,
    Driver,
    Connection,
    Services,
    Events,
    Counters,
    Firmware,
    Maintenance,
    LogReferences,
    Network
}

public enum EvidenceCorrelationKind
{
    ManufacturerSerial,
    HardwareInstance,
    StableUsbPath,
    AdministratorMapping
}

public static class EvidenceErrorCodes
{
    public const string DataUnknown = "data_unknown";
    public const string CapabilityUnsupported = "capability_unsupported";
    public const string ProviderUnavailable = "provider_unavailable";
    public const string PlatformUnavailable = "platform_unavailable";
    public const string AvailabilityFailed = "availability_failed";
    public const string DiscoveryFailed = "discovery_failed";
    public const string CollectionFailed = "collection_failed";
    public const string TargetNotFound = "target_not_found";
    public const string PathNotAllowed = "path_not_allowed";
    public const string SymbolicLinkNotAllowed = "symbolic_link_not_allowed";
    public const string FileTooLarge = "file_too_large";

    internal static string Normalize(string? errorCode, string fallback)
    {
        var candidate = errorCode?.Trim();
        return !string.IsNullOrWhiteSpace(candidate) && candidate.All(character =>
            char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '_')
                ? candidate
                : fallback;
    }
}

public sealed record EvidenceValue<T>
{
    private readonly T? _value;

    private EvidenceValue(EvidenceValueState state, T? value, string? errorCode)
    {
        State = state;
        _value = value;
        ErrorCode = errorCode;
    }

    public EvidenceValueState State { get; }

    public T Value => State == EvidenceValueState.Known
        ? _value!
        : throw new InvalidOperationException("Evidence has no known value.");

    public string? ErrorCode { get; }

    public static EvidenceValue<T> Known(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new EvidenceValue<T>(EvidenceValueState.Known, value, null);
    }

    public static EvidenceValue<T> Unknown(string? errorCode = null) =>
        new(EvidenceValueState.Unknown, default, EvidenceErrorCodes.Normalize(errorCode, EvidenceErrorCodes.DataUnknown));

    public static EvidenceValue<T> Unsupported() =>
        new(EvidenceValueState.Unsupported, default, EvidenceErrorCodes.CapabilityUnsupported);

    public static EvidenceValue<T> Unavailable(string? errorCode = null) =>
        new(
            EvidenceValueState.Unavailable,
            default,
            EvidenceErrorCodes.Normalize(errorCode, EvidenceErrorCodes.ProviderUnavailable));

    public static EvidenceValue<T> Failed(string? errorCode = null) =>
        new(
            EvidenceValueState.Failed,
            default,
            EvidenceErrorCodes.Normalize(errorCode, EvidenceErrorCodes.CollectionFailed));
}

public sealed record EvidenceSourceDescriptor(
    string ProviderId,
    string DisplayName,
    string SourceType,
    EvidenceSourceQuality SourceQuality,
    bool DevelopmentOnly,
    ImmutableArray<EvidenceCapability> Capabilities);

public sealed record EvidenceAvailability(EvidenceValueState State, string? ErrorCode)
{
    public static EvidenceAvailability Available() => new(EvidenceValueState.Known, null);

    public static EvidenceAvailability Unavailable(string? errorCode = null) =>
        new(
            EvidenceValueState.Unavailable,
            EvidenceErrorCodes.Normalize(errorCode, EvidenceErrorCodes.ProviderUnavailable));

    public static EvidenceAvailability Failed(string? errorCode = null) =>
        new(EvidenceValueState.Failed, EvidenceErrorCodes.Normalize(errorCode, EvidenceErrorCodes.AvailabilityFailed));
}

public sealed record EvidenceCorrelationKey(
    EvidenceCorrelationKind Kind,
    string ValueHash);

public sealed record ScannerEvidenceTarget(
    string TargetId,
    string ProviderId,
    ImmutableArray<EvidenceCorrelationKey> CorrelationKeys);

public sealed record DeviceIdentityEvidence(
    EvidenceValue<string> Manufacturer,
    EvidenceValue<string> Model,
    EvidenceValue<string> SerialNumber,
    EvidenceValue<string> HardwareInstanceId,
    EvidenceValue<string> UsbVendorId,
    EvidenceValue<string> UsbProductId);

public sealed record DriverEvidence(
    EvidenceValue<string> PackageName,
    EvidenceValue<string> Version,
    EvidenceValue<string> Provider,
    EvidenceValue<DateTimeOffset> DriverDate);

public sealed record ConnectionEvidence(
    EvidenceValue<bool> Present,
    EvidenceValue<string> StableUsbPath,
    EvidenceValue<DateTimeOffset> LastArrivalUtc,
    EvidenceValue<DateTimeOffset> LastRemovalUtc);

public enum EvidenceServiceState
{
    Running,
    Stopped,
    Paused,
    StartPending,
    StopPending
}

public sealed record ServiceEvidence(
    string ServiceName,
    EvidenceValue<EvidenceServiceState> State,
    EvidenceValue<string> Version);

public enum EvidenceEventKind
{
    DeviceArrival,
    DeviceRemoval,
    UsbControllerReset,
    DriverFailure,
    ServiceFailure,
    ApplicationCrash
}

public sealed record EventEvidence(
    EvidenceEventKind Kind,
    string StableEventCode,
    EvidenceValue<DateTimeOffset> OccurredAtUtc,
    EvidenceValue<string> ReferenceId);

public sealed record CounterEvidence(
    ImmutableDictionary<string, EvidenceValue<long>> Counters);

public sealed record FirmwareEvidence(EvidenceValue<string> Version);

public sealed record MaintenanceEvidence(
    ImmutableDictionary<string, EvidenceValue<string>> Values);

public sealed record LogEvidenceReference(
    string ReferenceId,
    EvidenceValue<bool> Exists,
    EvidenceValue<DateTimeOffset> LastModifiedUtc,
    EvidenceValue<long> SizeBytes,
    ImmutableArray<string> StableErrorCodes);

public sealed record NetworkEvidence(
    EvidenceValue<bool> Present,
    EvidenceValue<string> Firmware,
    EvidenceValue<string> SerialNumber,
    EvidenceValue<CounterEvidence> Counters,
    EvidenceValue<TimeSpan> Uptime,
    EvidenceValue<string> ErrorState);

public sealed record EvidenceProvenance(
    string ProviderId,
    string SourceType,
    EvidenceSourceQuality SourceQuality,
    string TargetId);

public sealed record ScannerEvidenceObservation(
    EvidenceSourceDescriptor Source,
    ScannerEvidenceTarget Target,
    DateTimeOffset CollectedAtUtc,
    EvidenceProvenance Provenance,
    EvidenceValue<DeviceIdentityEvidence> Identity,
    EvidenceValue<DriverEvidence> Driver,
    EvidenceValue<ConnectionEvidence> Connection,
    EvidenceValue<ImmutableArray<ServiceEvidence>> Services,
    EvidenceValue<ImmutableArray<EventEvidence>> Events,
    EvidenceValue<CounterEvidence> Counters,
    EvidenceValue<FirmwareEvidence> Firmware,
    EvidenceValue<MaintenanceEvidence> Maintenance,
    EvidenceValue<ImmutableArray<LogEvidenceReference>> LogReferences,
    EvidenceValue<NetworkEvidence> Network);

public sealed record ScannerEvidenceSnapshot(
    string ScannerId,
    EvidenceValue<DeviceIdentityEvidence> Identity,
    EvidenceValue<DriverEvidence> Driver,
    EvidenceValue<ConnectionEvidence> Connection,
    EvidenceValue<ImmutableArray<ServiceEvidence>> Services,
    EvidenceValue<ImmutableArray<EventEvidence>> Events,
    EvidenceValue<CounterEvidence> Counters,
    EvidenceValue<FirmwareEvidence> Firmware,
    EvidenceValue<MaintenanceEvidence> Maintenance,
    EvidenceValue<ImmutableArray<LogEvidenceReference>> LogReferences,
    EvidenceValue<NetworkEvidence> Network,
    ImmutableArray<ScannerEvidenceObservation> Observations,
    ImmutableArray<EvidenceProvenance> Provenance);

public sealed record EvidenceProviderDiagnostic(
    string ProviderId,
    string Operation,
    EvidenceValueState State,
    string? ErrorCode);

public sealed record ScannerEvidenceCollectionSnapshot(
    DateTimeOffset CollectedAtUtc,
    ImmutableArray<ScannerEvidenceSnapshot> Scanners,
    ImmutableArray<EvidenceProviderDiagnostic> Diagnostics);
