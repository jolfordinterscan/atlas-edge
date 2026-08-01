namespace Atlas.Edge.ScannerDiscovery;

public sealed record ScannerSourceMetadata(
    string SourceId,
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
    ScannerOnlineStatus OnlineStatus,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public string? DevicePath { get; init; }

    public bool HasProviderStableIdentity { get; init; } = true;
}

public sealed record ScannerSourceCatalogResult(
    bool IsAvailable,
    IReadOnlyList<ScannerSourceMetadata> Sources);

public interface IWiaScannerSourceCatalog
{
    Task<ScannerSourceCatalogResult> EnumerateAsync(CancellationToken cancellationToken);
}

public interface ITwainScannerSourceCatalog
{
    Task<ScannerSourceCatalogResult> EnumerateAsync(CancellationToken cancellationToken);
}

public interface IIsisScannerSourceCatalog
{
    Task<ScannerSourceCatalogResult> EnumerateAsync(CancellationToken cancellationToken);
}
