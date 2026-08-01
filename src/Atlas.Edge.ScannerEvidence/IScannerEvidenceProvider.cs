using System.Collections.Immutable;

namespace Atlas.Edge.ScannerEvidence;

public interface IScannerEvidenceProvider
{
    EvidenceSourceDescriptor Descriptor { get; }

    Task<EvidenceAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken);

    Task<EvidenceValue<ImmutableArray<ScannerEvidenceTarget>>> DiscoverTargetsAsync(
        CancellationToken cancellationToken);

    Task<EvidenceValue<DeviceIdentityEvidence>> ReadIdentityAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken);

    Task<EvidenceValue<DriverEvidence>> ReadDriverAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken);

    Task<EvidenceValue<ConnectionEvidence>> ReadConnectionAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken);

    Task<EvidenceValue<ImmutableArray<ServiceEvidence>>> ReadServicesAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken);

    Task<EvidenceValue<ImmutableArray<EventEvidence>>> ReadEventsAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken);

    Task<EvidenceValue<CounterEvidence>> ReadCountersAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken);

    Task<EvidenceValue<FirmwareEvidence>> ReadFirmwareAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken);

    Task<EvidenceValue<MaintenanceEvidence>> ReadMaintenanceAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken);

    Task<EvidenceValue<ImmutableArray<LogEvidenceReference>>> ReadLogReferencesAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken);

    Task<EvidenceValue<NetworkEvidence>> ReadNetworkAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken);
}

public abstract class ScannerEvidenceProviderBase : IScannerEvidenceProvider
{
    public abstract EvidenceSourceDescriptor Descriptor { get; }

    public abstract Task<EvidenceAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken);

    public abstract Task<EvidenceValue<ImmutableArray<ScannerEvidenceTarget>>> DiscoverTargetsAsync(
        CancellationToken cancellationToken);

    public virtual Task<EvidenceValue<DeviceIdentityEvidence>> ReadIdentityAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken) => Unsupported<DeviceIdentityEvidence>(cancellationToken);

    public virtual Task<EvidenceValue<DriverEvidence>> ReadDriverAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken) => Unsupported<DriverEvidence>(cancellationToken);

    public virtual Task<EvidenceValue<ConnectionEvidence>> ReadConnectionAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken) => Unsupported<ConnectionEvidence>(cancellationToken);

    public virtual Task<EvidenceValue<ImmutableArray<ServiceEvidence>>> ReadServicesAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken) => Unsupported<ImmutableArray<ServiceEvidence>>(cancellationToken);

    public virtual Task<EvidenceValue<ImmutableArray<EventEvidence>>> ReadEventsAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken) => Unsupported<ImmutableArray<EventEvidence>>(cancellationToken);

    public virtual Task<EvidenceValue<CounterEvidence>> ReadCountersAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken) => Unsupported<CounterEvidence>(cancellationToken);

    public virtual Task<EvidenceValue<FirmwareEvidence>> ReadFirmwareAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken) => Unsupported<FirmwareEvidence>(cancellationToken);

    public virtual Task<EvidenceValue<MaintenanceEvidence>> ReadMaintenanceAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken) => Unsupported<MaintenanceEvidence>(cancellationToken);

    public virtual Task<EvidenceValue<ImmutableArray<LogEvidenceReference>>> ReadLogReferencesAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken) => Unsupported<ImmutableArray<LogEvidenceReference>>(cancellationToken);

    public virtual Task<EvidenceValue<NetworkEvidence>> ReadNetworkAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken) => Unsupported<NetworkEvidence>(cancellationToken);

    private static Task<EvidenceValue<T>> Unsupported<T>(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(EvidenceValue<T>.Unsupported());
    }
}
