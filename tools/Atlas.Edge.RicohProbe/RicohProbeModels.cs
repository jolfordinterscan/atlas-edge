using System.Text.Json.Serialization;

namespace Atlas.Edge.RicohProbe;

public enum RicohProbeOperation
{
    None,
    Check,
    ReadSerial,
    ListSources
}

public sealed record RicohProbeRequest(
    RicohProbeOperation Operation,
    string? SourceName = null,
    string? Manufacturer = null,
    string? Model = null,
    string? UsbVendorId = null,
    string? UsbProductId = null,
    bool Verbose = false);

public sealed record RicohSdkEnumeratedSource(int Index, string Name);

public sealed record RicohSdkSourceEnumeration(
    int CountResult,
    int SelectedIndexResult,
    IReadOnlyList<RicohSdkEnumeratedSource> Sources);

public sealed record RicohSourceDriverAssociation(
    string EnvironmentSource,
    string EnvironmentKind,
    string DriverName,
    string? DriverVersion,
    string? DriverProvider,
    string Architecture,
    string MatchBasis);

public sealed record RicohSdkSourceDiagnostic(
    int Index,
    string Name,
    bool IsSelected,
    string SourceType,
    RicohSourceDriverAssociation? DriverAssociation,
    int? SdkErrorCode,
    bool SdkErrorCodeAvailable);

public sealed record RicohEnvironmentSource(
    string Kind,
    string Name,
    string Manufacturer,
    string DriverName,
    string? DriverVersion,
    string? DriverProvider,
    string Architecture);

public sealed record RicohSourceEnvironmentSnapshot(
    bool WiaAvailable,
    bool PnpAvailable,
    bool TwainAvailable,
    IReadOnlyList<RicohEnvironmentSource> Sources,
    IReadOnlyList<string> Diagnostics)
{
    public static RicohSourceEnvironmentSnapshot Empty { get; } = new(false, false, false, [], []);
}

public sealed record RicohRuntimeAvailability(
    bool IsWindows,
    bool IsX64,
    bool IsRuntimeRegistered,
    bool IsSdkBuildEnabled,
    string RuntimeVersion = "Unknown",
    bool IsX86 = false)
{
    public string ProcessArchitecture => IsX64 ? "X64" : IsX86 ? "X86" : "Unknown";
}

public sealed record RicohSerialProbeResult
{
    public string SchemaVersion { get; init; } = "1.0";
    public string Operation { get; init; } = "None";
    public bool SdkAvailable { get; init; }
    public string Architecture { get; init; } = "Unknown";
    public string RuntimeVersion { get; init; } = "Unknown";
    public int SourceCount { get; init; }
    public IReadOnlyList<string> Sources { get; init; } = [];
    public int? SelectedSourceIndex { get; init; }
    public IReadOnlyList<RicohSdkSourceDiagnostic> SdkSources { get; init; } = [];
    public RicohSourceEnvironmentSnapshot? EnvironmentSources { get; init; }
    public bool EnumerationErrorCodeAvailable { get; init; }
    public string? SelectedSource { get; init; }
    public int? OpenResult { get; init; }
    public int? OpenErrorCode { get; init; }
    public bool SerialAvailable { get; init; }
    public string? SerialNumber { get; init; }
    public string? MaskedSerialNumber { get; init; }
    public string? SerialSource { get; init; }
    public int? SerialErrorCode { get; init; }
    public bool CloseAttempted { get; init; }
    public int? CloseResult { get; init; }
    public int? CloseErrorCode { get; init; }
    public bool ScannerClosed { get; init; }
    public long DurationMs { get; init; }
    public string Status { get; init; } = "Failed";
    public string? DiagnosticCode { get; init; }
}

public static class RicohProbeError
{
    public const string ExplicitModeRequired = "ricoh_probe_explicit_mode_required";
    public const string NotWindows = "ricoh_probe_not_windows";
    public const string NotX64 = "ricoh_probe_not_x64";
    public const string UnsupportedArchitecture = "ricoh_probe_unsupported_architecture";
    public const string SdkUnavailable = "ricoh_sdk_unavailable";
    public const string ActiveXCreationFailed = "ricoh_activex_creation_failed";
    public const string HiddenHostFailed = "ricoh_hidden_host_failed";
    public const string SourceNotFound = "ricoh_source_not_found";
    public const string SourceAmbiguous = "ricoh_source_ambiguous";
    public const string SourceUnsupported = "ricoh_source_unsupported";
    public const string SourceSelectionFailed = "ricoh_source_selection_failed";
    public const string OpenFailed = "ricoh_open_failed";
    public const string ScannerBusy = "ricoh_scanner_busy";
    public const string SerialEmpty = "ricoh_serial_empty";
    public const string SerialInvalid = "ricoh_serial_invalid";
    public const string SerialReadFailed = "ricoh_serial_read_failed";
    public const string CloseFailed = "ricoh_close_failed";
    public const string Timeout = "ricoh_probe_timeout";
    public const string SessionActive = "ricoh_probe_session_active";
    public const string UnhandledFailure = "ricoh_probe_unhandled_failure";
    public const string SourceEnumerationFailed = "ricoh_source_enumeration_failed";
}

public static class RicohProbeJson
{
    private static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = true
    };

    public static string Serialize(RicohSerialProbeResult result) =>
        System.Text.Json.JsonSerializer.Serialize(result, Options);
}
