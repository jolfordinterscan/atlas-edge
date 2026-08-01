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

public sealed record ScannerStableIdentity(
    string ScannerId,
    string ProviderId,
    string? DevicePathHash,
    ScannerMetadataConfidence Confidence);
