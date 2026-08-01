namespace Atlas.Edge.ScannerHealth;

public interface IScannerHealthProvider
{
    Atlas.Edge.ScannerDiscovery.ScannerProtocol Protocol { get; }

    Task<ScannerHealthProviderResult> CollectAsync(CancellationToken cancellationToken);
}

public interface IScannerHealthService
{
    Task<ScannerHealthCollectionSnapshot> CollectAsync(CancellationToken cancellationToken);
}
