using System.Collections.Immutable;
using Atlas.Edge.ScannerConnectors;
using Atlas.Edge.ScannerDiscovery;

namespace Atlas.Edge.ScannerEvidence;

public sealed class WiaScannerEvidenceProvider : ConnectorEvidenceProvider
{
    public WiaScannerEvidenceProvider(IWiaScannerSourceCatalog catalog)
        : base(new WiaScannerConnector(catalog))
    {
    }
}

public sealed class TwainScannerEvidenceProvider : ConnectorEvidenceProvider
{
    public TwainScannerEvidenceProvider(ITwainScannerSourceCatalog catalog)
        : base(new TwainScannerConnector(catalog))
    {
    }
}

public sealed class IsisScannerEvidenceProvider : ConnectorEvidenceProvider
{
    public IsisScannerEvidenceProvider(IIsisScannerSourceCatalog catalog)
        : base(new IsisScannerConnector(catalog))
    {
    }
}

public sealed class DevelopmentMockEvidenceProvider : ConnectorEvidenceProvider
{
    public DevelopmentMockEvidenceProvider()
        : base(new DevelopmentMockScannerConnector(), sourceQuality: EvidenceSourceQuality.UserConfigured)
    {
    }
}

public class ConnectorEvidenceProvider : ScannerEvidenceProviderBase, IDisposable
{
    private readonly IScannerConnector _connector;
    private readonly bool _ownsConnector;

    public ConnectorEvidenceProvider(
        IScannerConnector connector,
        bool ownsConnector = true,
        EvidenceSourceQuality sourceQuality = EvidenceSourceQuality.StandardProtocol)
    {
        _connector = connector;
        _ownsConnector = ownsConnector;
        Descriptor = CreateDescriptor(connector.Descriptor, sourceQuality);
    }

    public override EvidenceSourceDescriptor Descriptor { get; }

    public override async Task<EvidenceAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        var result = await _connector.CheckAvailabilityAsync(cancellationToken);
        return result.State switch
        {
            ConnectorResultState.Known => EvidenceAvailability.Available(),
            ConnectorResultState.Unavailable => EvidenceAvailability.Unavailable(result.ErrorCode),
            _ => EvidenceAvailability.Failed(result.ErrorCode)
        };
    }

    public override async Task<EvidenceValue<ImmutableArray<ScannerEvidenceTarget>>> DiscoverTargetsAsync(
        CancellationToken cancellationToken)
    {
        var result = await _connector.DiscoverAsync(cancellationToken);
        return Map(result, targets => targets.Select(target => new ScannerEvidenceTarget(
            target.TargetId,
            Descriptor.ProviderId,
            ImmutableArray<EvidenceCorrelationKey>.Empty)).ToImmutableArray());
    }

    public override async Task<EvidenceValue<DeviceIdentityEvidence>> ReadIdentityAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken)
    {
        var result = await _connector.ReadIdentityAsync(ToConnectorTarget(target), cancellationToken);
        return Map(result, identity => new DeviceIdentityEvidence(
            Map(identity.Manufacturer),
            Map(identity.Model),
            Map(identity.SerialNumber),
            EvidenceValue<string>.Unknown(),
            EvidenceValue<string>.Unknown(),
            EvidenceValue<string>.Unknown()));
    }

    public override async Task<EvidenceValue<DriverEvidence>> ReadDriverAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken)
    {
        var result = await _connector.ReadIdentityAsync(ToConnectorTarget(target), cancellationToken);
        return Map(result, identity => new DriverEvidence(
            Map(identity.DriverName),
            Map(identity.DriverVersion),
            EvidenceValue<string>.Unknown(),
            EvidenceValue<DateTimeOffset>.Unknown()));
    }

    public override async Task<EvidenceValue<ConnectionEvidence>> ReadConnectionAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken)
    {
        var result = await _connector.ReadCurrentStatusAsync(ToConnectorTarget(target), cancellationToken);
        return Map(result, status => new ConnectionEvidence(
            status.OnlineStatus.State == ConnectorResultState.Known
                ? EvidenceValue<bool>.Known(status.OnlineStatus.Value == ConnectorScannerOnlineStatus.Online)
                : MapUnknown<bool>(status.OnlineStatus.State, status.OnlineStatus.ErrorCode),
            EvidenceValue<string>.Unknown(),
            EvidenceValue<DateTimeOffset>.Unknown(),
            EvidenceValue<DateTimeOffset>.Unknown()));
    }

    public override async Task<EvidenceValue<CounterEvidence>> ReadCountersAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken)
    {
        var result = await _connector.ReadCountersAsync(ToConnectorTarget(target), cancellationToken);
        return Map(result, counters => new CounterEvidence(
            ImmutableDictionary<string, EvidenceValue<long>>.Empty
                .Add("lifetime_pages", Map(counters.LifetimePages))
                .Add("daily_pages", Map(counters.DailyPages))
                .Add("jam_count", Map(counters.JamCount))
                .Add("double_feed_count", Map(counters.DoubleFeedCount))
                .Add("transport_error_count", Map(counters.TransportErrorCount))));
    }

    public override async Task<EvidenceValue<FirmwareEvidence>> ReadFirmwareAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken)
    {
        var result = await _connector.ReadFirmwareAsync(ToConnectorTarget(target), cancellationToken);
        return Map(result, firmware => new FirmwareEvidence(Map(firmware.Version)));
    }

    public override async Task<EvidenceValue<MaintenanceEvidence>> ReadMaintenanceAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken)
    {
        var result = await _connector.ReadDiagnosticsAsync(ToConnectorTarget(target), cancellationToken);
        return Map(result, diagnostics => new MaintenanceEvidence(
            diagnostics.MaintenanceCounters.State == ConnectorResultState.Known
                ? diagnostics.MaintenanceCounters.Value!.ToImmutableDictionary(
                    item => item.Key,
                    item => EvidenceValue<string>.Known(item.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    StringComparer.OrdinalIgnoreCase)
                : ImmutableDictionary<string, EvidenceValue<string>>.Empty));
    }

    public override async Task<EvidenceValue<ImmutableArray<LogEvidenceReference>>> ReadLogReferencesAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken)
    {
        var result = await _connector.ReadLogReferencesAsync(ToConnectorTarget(target), cancellationToken);
        return Map(result, references => references.Select(reference => new LogEvidenceReference(
            reference.ReferenceId,
            EvidenceValue<bool>.Unknown(),
            EvidenceValue<DateTimeOffset>.Unknown(),
            EvidenceValue<long>.Unknown(),
            ImmutableArray<string>.Empty)).ToImmutableArray());
    }

    public void Dispose()
    {
        if (_ownsConnector && _connector is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private static EvidenceSourceDescriptor CreateDescriptor(
        ConnectorDescriptor descriptor,
        EvidenceSourceQuality sourceQuality)
    {
        var capabilities = descriptor.Capabilities
            .Select(MapCapability)
            .Where(capability => capability.HasValue)
            .Select(capability => capability!.Value)
            .Append(EvidenceCapability.Discovery)
            .Distinct()
            .ToImmutableArray();
        return new EvidenceSourceDescriptor(
            $"connector_{descriptor.ConnectorId}",
            $"{descriptor.DisplayName} Evidence",
            descriptor.Protocol,
            sourceQuality,
            descriptor.DevelopmentOnly,
            capabilities);
    }

    private static EvidenceCapability? MapCapability(ConnectorCapability capability) => capability switch
    {
        ConnectorCapability.Discovery => EvidenceCapability.Discovery,
        ConnectorCapability.Identity => EvidenceCapability.DeviceIdentity,
        ConnectorCapability.Firmware => EvidenceCapability.Firmware,
        ConnectorCapability.Counters => EvidenceCapability.Counters,
        ConnectorCapability.CurrentStatus => EvidenceCapability.Connection,
        ConnectorCapability.Diagnostics => EvidenceCapability.Maintenance,
        ConnectorCapability.LogReferences => EvidenceCapability.LogReferences,
        _ => null
    };

    private ScannerConnectionTarget ToConnectorTarget(ScannerEvidenceTarget target) =>
        new(target.TargetId, _connector.Descriptor.ConnectorId);

    private static EvidenceValue<TOutput> Map<TInput, TOutput>(
        ConnectorValue<TInput> source,
        Func<TInput, TOutput> map)
    {
        return source.State switch
        {
            ConnectorResultState.Known => EvidenceValue<TOutput>.Known(map(source.Value!)),
            ConnectorResultState.Unknown => EvidenceValue<TOutput>.Unknown(source.ErrorCode),
            ConnectorResultState.Unsupported => EvidenceValue<TOutput>.Unsupported(),
            ConnectorResultState.Unavailable => EvidenceValue<TOutput>.Unavailable(source.ErrorCode),
            _ => EvidenceValue<TOutput>.Failed(source.ErrorCode)
        };
    }

    private static EvidenceValue<T> Map<T>(ConnectorValue<T> source) =>
        source.State switch
        {
            ConnectorResultState.Known => EvidenceValue<T>.Known(source.Value!),
            ConnectorResultState.Unknown => EvidenceValue<T>.Unknown(source.ErrorCode),
            ConnectorResultState.Unsupported => EvidenceValue<T>.Unsupported(),
            ConnectorResultState.Unavailable => EvidenceValue<T>.Unavailable(source.ErrorCode),
            _ => EvidenceValue<T>.Failed(source.ErrorCode)
        };

    private static EvidenceValue<T> MapUnknown<T>(ConnectorResultState state, string? errorCode) => state switch
    {
        ConnectorResultState.Unsupported => EvidenceValue<T>.Unsupported(),
        ConnectorResultState.Unavailable => EvidenceValue<T>.Unavailable(errorCode),
        ConnectorResultState.Failed => EvidenceValue<T>.Failed(errorCode),
        _ => EvidenceValue<T>.Unknown(errorCode)
    };
}
