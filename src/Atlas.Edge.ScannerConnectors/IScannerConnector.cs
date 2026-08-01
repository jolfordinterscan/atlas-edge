using System.Collections.Immutable;

namespace Atlas.Edge.ScannerConnectors;

public interface IScannerConnector
{
    ConnectorDescriptor Descriptor { get; }

    Task<ConnectorAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken);

    Task<ConnectorValue<ImmutableArray<ScannerConnectionTarget>>> DiscoverAsync(
        CancellationToken cancellationToken);

    Task<ConnectorValue<ScannerIdentity>> ReadIdentityAsync(
        ScannerConnectionTarget target,
        CancellationToken cancellationToken);

    Task<ConnectorValue<ScannerCapabilities>> ReadCapabilitiesAsync(
        ScannerConnectionTarget target,
        CancellationToken cancellationToken);

    Task<ConnectorValue<ScannerFirmware>> ReadFirmwareAsync(
        ScannerConnectionTarget target,
        CancellationToken cancellationToken);

    Task<ConnectorValue<ScannerCounters>> ReadCountersAsync(
        ScannerConnectionTarget target,
        CancellationToken cancellationToken);

    Task<ConnectorValue<ScannerHealth>> ReadHealthAsync(
        ScannerConnectionTarget target,
        CancellationToken cancellationToken);

    Task<ConnectorValue<ScannerStatus>> ReadCurrentStatusAsync(
        ScannerConnectionTarget target,
        CancellationToken cancellationToken);

    Task<ConnectorValue<ScannerDiagnostics>> ReadDiagnosticsAsync(
        ScannerConnectionTarget target,
        CancellationToken cancellationToken);

    Task<ConnectorValue<ImmutableArray<ScannerLogReference>>> ReadLogReferencesAsync(
        ScannerConnectionTarget target,
        CancellationToken cancellationToken);
}

public abstract class ScannerConnectorBase : IScannerConnector
{
    public abstract ConnectorDescriptor Descriptor { get; }

    public abstract Task<ConnectorAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken);

    public abstract Task<ConnectorValue<ImmutableArray<ScannerConnectionTarget>>> DiscoverAsync(
        CancellationToken cancellationToken);

    public virtual Task<ConnectorValue<ScannerIdentity>> ReadIdentityAsync(
        ScannerConnectionTarget target,
        CancellationToken cancellationToken) =>
        Unsupported<ScannerIdentity>(cancellationToken);

    public virtual Task<ConnectorValue<ScannerCapabilities>> ReadCapabilitiesAsync(
        ScannerConnectionTarget target,
        CancellationToken cancellationToken) =>
        Unsupported<ScannerCapabilities>(cancellationToken);

    public virtual Task<ConnectorValue<ScannerFirmware>> ReadFirmwareAsync(
        ScannerConnectionTarget target,
        CancellationToken cancellationToken) =>
        Unsupported<ScannerFirmware>(cancellationToken);

    public virtual Task<ConnectorValue<ScannerCounters>> ReadCountersAsync(
        ScannerConnectionTarget target,
        CancellationToken cancellationToken) =>
        Unsupported<ScannerCounters>(cancellationToken);

    public virtual Task<ConnectorValue<ScannerHealth>> ReadHealthAsync(
        ScannerConnectionTarget target,
        CancellationToken cancellationToken) =>
        Unsupported<ScannerHealth>(cancellationToken);

    public virtual Task<ConnectorValue<ScannerStatus>> ReadCurrentStatusAsync(
        ScannerConnectionTarget target,
        CancellationToken cancellationToken) =>
        Unsupported<ScannerStatus>(cancellationToken);

    public virtual Task<ConnectorValue<ScannerDiagnostics>> ReadDiagnosticsAsync(
        ScannerConnectionTarget target,
        CancellationToken cancellationToken) =>
        Unsupported<ScannerDiagnostics>(cancellationToken);

    public virtual Task<ConnectorValue<ImmutableArray<ScannerLogReference>>> ReadLogReferencesAsync(
        ScannerConnectionTarget target,
        CancellationToken cancellationToken) =>
        Unsupported<ImmutableArray<ScannerLogReference>>(cancellationToken);

    private static Task<ConnectorValue<T>> Unsupported<T>(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ConnectorValue<T>.Unsupported());
    }
}
