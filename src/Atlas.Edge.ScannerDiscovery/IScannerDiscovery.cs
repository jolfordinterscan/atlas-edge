namespace Atlas.Edge.ScannerDiscovery;

public interface IScannerDiscoveryAdapter
{
    ScannerProtocol Protocol { get; }

    Task<ScannerAdapterResult> DiscoverAsync(CancellationToken cancellationToken);
}

public interface IScannerDiscoveryService
{
    Task<ScannerDiscoverySnapshot> DiscoverAsync(CancellationToken cancellationToken);
}

public interface IScannerIdentityFactory
{
    ScannerStableIdentity Create(AdapterScannerDevice scanner);
}

public interface IScannerMetadataProvider
{
    string ProviderName { get; }

    Task<ScannerMetadataProviderResult> GetMetadataAsync(
        AdapterScannerDevice scanner,
        CancellationToken cancellationToken);
}

public interface IPnpScannerMetadataProvider : IScannerMetadataProvider
{
}

public interface IRegistryScannerMetadataProvider : IScannerMetadataProvider
{
}

public interface IScannerMetadataEnricher
{
    Task<IReadOnlyList<AdapterScannerDevice>> EnrichAsync(
        IReadOnlyList<AdapterScannerDevice> scanners,
        CancellationToken cancellationToken);
}

public sealed record ScannerMetadataProviderResult(
    bool IsAvailable,
    ScannerMetadata? Metadata,
    string? ErrorCode)
{
    public ScannerMetadataMatchDiagnostic? Diagnostic { get; init; }

    public static ScannerMetadataProviderResult Available(ScannerMetadata? metadata) =>
        new(true, metadata, null);

    public static ScannerMetadataProviderResult Unavailable(string errorCode) =>
        new(false, null, errorCode);

    public static ScannerMetadataProviderResult Failed(string errorCode) =>
        new(true, null, errorCode);
}

public sealed record ScannerMetadataMatchDiagnostic(
    string ProviderName,
    string MatchStrategy,
    int MatchScore,
    int CandidatesEvaluated,
    bool IsAmbiguous,
    IReadOnlyList<string> PopulatedFields);

public sealed record ScannerStableIdentity(
    string ScannerId,
    string ProviderId,
    string? DevicePathHash,
    ScannerMetadataConfidence Confidence);
