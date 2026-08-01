using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Atlas.Edge.ScannerDiscovery;
using Atlas.Edge.ScannerHealth;

namespace Atlas.Edge.ScannerConnectors;

public sealed class WiaScannerConnector : ExistingScannerConnectorAdapter
{
    public WiaScannerConnector(IWiaScannerSourceCatalog catalog)
        : base(
            CreateDescriptor("wia", "WIA Scanner Connector", "WIA", developmentOnly: false),
            new WiaScannerDiscoveryAdapter(catalog),
            new WiaScannerHealthProvider(catalog))
    {
    }
}

public sealed class TwainScannerConnector : ExistingScannerConnectorAdapter
{
    public TwainScannerConnector(ITwainScannerSourceCatalog catalog)
        : base(
            CreateDescriptor("twain", "TWAIN Scanner Connector", "TWAIN", developmentOnly: false),
            new TwainScannerDiscoveryAdapter(catalog),
            new TwainScannerHealthProvider(catalog))
    {
    }
}

public sealed class IsisScannerConnector : ExistingScannerConnectorAdapter
{
    public IsisScannerConnector(IIsisScannerSourceCatalog catalog)
        : base(
            CreateDescriptor("isis", "ISIS Scanner Connector", "ISIS", developmentOnly: false),
            new IsisScannerDiscoveryAdapter(catalog),
            new IsisScannerHealthProvider(catalog))
    {
    }
}

public sealed class DevelopmentMockScannerConnector : ExistingScannerConnectorAdapter
{
    public DevelopmentMockScannerConnector()
        : base(
            CreateDescriptor(
                "development_mock",
                "Development Mock Scanner Connector",
                "Mock",
                developmentOnly: true,
                includesLogReferences: true),
            new MockScannerDiscoveryAdapter(),
            new MockScannerHealthProvider())
    {
    }

    public override Task<ConnectorValue<ImmutableArray<ScannerLogReference>>> ReadLogReferencesAsync(
        ScannerConnectionTarget target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ConnectorValue<ImmutableArray<ScannerLogReference>>.Known(
            [new ScannerLogReference("development-mock-log", "development-reference")]));
    }
}

public abstract class ExistingScannerConnectorAdapter : ScannerConnectorBase, IDisposable
{
    private readonly IScannerDiscoveryAdapter _discoveryAdapter;
    private readonly IScannerHealthProvider _healthProvider;
    private readonly SemaphoreSlim _discoveryGate = new(1, 1);
    private readonly SemaphoreSlim _healthGate = new(1, 1);
    private ImmutableDictionary<string, AdapterScannerDevice> _devices =
        ImmutableDictionary<string, AdapterScannerDevice>.Empty;
    private ScannerAdapterResult? _pendingDiscovery;
    private ScannerHealthProviderResult? _healthResult;
    private bool _disposed;

    protected ExistingScannerConnectorAdapter(
        ConnectorDescriptor descriptor,
        IScannerDiscoveryAdapter discoveryAdapter,
        IScannerHealthProvider healthProvider)
    {
        Descriptor = descriptor;
        _discoveryAdapter = discoveryAdapter;
        _healthProvider = healthProvider;
    }

    public override ConnectorDescriptor Descriptor { get; }

    public override async Task<ConnectorAvailability> CheckAvailabilityAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _discoveryGate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                _pendingDiscovery = await _discoveryAdapter.DiscoverAsync(cancellationToken);
                return _pendingDiscovery.IsAvailable
                    ? ConnectorAvailability.Available()
                    : ConnectorAvailability.Unavailable(_pendingDiscovery.ErrorCode);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                _pendingDiscovery = null;
                return ConnectorAvailability.Failed();
            }
        }
        finally
        {
            _discoveryGate.Release();
        }
    }

    public override async Task<ConnectorValue<ImmutableArray<ScannerConnectionTarget>>> DiscoverAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _discoveryGate.WaitAsync(cancellationToken);
        try
        {
            ScannerAdapterResult result;
            try
            {
                result = _pendingDiscovery ?? await _discoveryAdapter.DiscoverAsync(cancellationToken);
                _pendingDiscovery = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return ConnectorValue<ImmutableArray<ScannerConnectionTarget>>.Failed(
                    ConnectorErrorCodes.DiscoveryFailed);
            }

            if (!result.IsAvailable)
            {
                return ConnectorValue<ImmutableArray<ScannerConnectionTarget>>.Unavailable(result.ErrorCode);
            }

            if (!string.IsNullOrWhiteSpace(result.ErrorCode))
            {
                return ConnectorValue<ImmutableArray<ScannerConnectionTarget>>.Failed(result.ErrorCode);
            }

            var devices = ImmutableDictionary.CreateBuilder<string, AdapterScannerDevice>(StringComparer.Ordinal);
            foreach (var device in result.Devices)
            {
                devices[CreateTargetId(Descriptor.ConnectorId, device.SourceId)] = device;
            }

            _devices = devices.ToImmutable();
            _healthResult = null;
            return ConnectorValue<ImmutableArray<ScannerConnectionTarget>>.Known(
                _devices.Keys
                    .OrderBy(targetId => targetId, StringComparer.Ordinal)
                    .Select(targetId => new ScannerConnectionTarget(targetId, Descriptor.ConnectorId))
                    .ToImmutableArray());
        }
        finally
        {
            _discoveryGate.Release();
        }
    }

    public override Task<ConnectorValue<ScannerIdentity>> ReadIdentityAsync(
        ScannerConnectionTarget target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(TryGetDevice(target, out var device)
            ? ConnectorValue<ScannerIdentity>.Known(new ScannerIdentity(
                Text(device.Manufacturer),
                Text(device.Model),
                Text(device.SerialNumber),
                Text(device.Interface),
                Text(device.Driver.Name),
                Text(device.Driver.Version)))
            : ConnectorValue<ScannerIdentity>.Failed(ConnectorErrorCodes.TargetNotFound));
    }

    public override Task<ConnectorValue<ScannerCapabilities>> ReadCapabilitiesAsync(
        ScannerConnectionTarget target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(TryGetDevice(target, out var device)
            ? ConnectorValue<ScannerCapabilities>.Known(new ScannerCapabilities(
                Boolean(device.SupportsDuplex),
                Boolean(device.SupportsColor),
                Boolean(device.HasFeeder),
                device.Capabilities
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToImmutableArray()))
            : ConnectorValue<ScannerCapabilities>.Failed(ConnectorErrorCodes.TargetNotFound));
    }

    public override async Task<ConnectorValue<ScannerFirmware>> ReadFirmwareAsync(
        ScannerConnectionTarget target,
        CancellationToken cancellationToken)
    {
        if (!TryGetDevice(target, out var device))
        {
            return ConnectorValue<ScannerFirmware>.Failed(ConnectorErrorCodes.TargetNotFound);
        }

        if (!string.IsNullOrWhiteSpace(device.FirmwareVersion))
        {
            return ConnectorValue<ScannerFirmware>.Known(new ScannerFirmware(Text(device.FirmwareVersion)));
        }

        var health = await ReadHealthReadingAsync(target, cancellationToken);
        return health.State switch
        {
            ConnectorResultState.Known => ConnectorValue<ScannerFirmware>.Known(
                new ScannerFirmware(Text(health.Value!.FirmwareVersion))),
            ConnectorResultState.Unavailable => ConnectorValue<ScannerFirmware>.Unavailable(health.ErrorCode),
            ConnectorResultState.Failed => ConnectorValue<ScannerFirmware>.Failed(health.ErrorCode),
            _ => ConnectorValue<ScannerFirmware>.Known(new ScannerFirmware(ConnectorValue<string>.Unknown()))
        };
    }

    public override async Task<ConnectorValue<ScannerCounters>> ReadCountersAsync(
        ScannerConnectionTarget target,
        CancellationToken cancellationToken)
    {
        var result = await ReadHealthReadingAsync(target, cancellationToken);
        return MapReading(result, reading => new ScannerCounters(
            Number(reading.LifetimePages),
            Number(reading.DailyPages),
            Number(reading.JamCount),
            Number(reading.DoubleFeedCount),
            Number(reading.TransportErrorCount),
            reading.MaintenanceCountersKnown
                ? ConnectorValue<ImmutableDictionary<string, long>>.Known(reading.MaintenanceCounters)
                : ConnectorValue<ImmutableDictionary<string, long>>.Unknown()));
    }

    public override async Task<ConnectorValue<ScannerHealth>> ReadHealthAsync(
        ScannerConnectionTarget target,
        CancellationToken cancellationToken)
    {
        var result = await ReadHealthReadingAsync(target, cancellationToken);
        return MapReading(result, reading => new ScannerHealth(
            Number(reading.RollerLifePercent),
            Number(reading.PadLifePercent),
            Number(reading.ScanSpeedPagesPerMinute),
            Number(reading.RatedScanSpeedPagesPerMinute),
            Number(reading.UsbStability?.DisconnectCount),
            Date(reading.UsbStability?.LastDisconnectUtc),
            Duration(reading.DeviceUptime),
            reading.ConsumablesKnown
                ? ConnectorValue<ImmutableArray<ScannerConsumable>>.Known(
                    reading.Consumables.Select(consumable => new ScannerConsumable(
                        consumable.Name,
                        Number(consumable.RemainingPercent),
                        Text(consumable.Status))).ToImmutableArray())
                : ConnectorValue<ImmutableArray<ScannerConsumable>>.Unknown()));
    }

    public override async Task<ConnectorValue<ScannerStatus>> ReadCurrentStatusAsync(
        ScannerConnectionTarget target,
        CancellationToken cancellationToken)
    {
        if (!TryGetDevice(target, out var device))
        {
            return ConnectorValue<ScannerStatus>.Failed(ConnectorErrorCodes.TargetNotFound);
        }

        var health = await ReadHealthReadingAsync(target, cancellationToken);
        var driverStatus = health.State == ConnectorResultState.Known
            ? DriverStatus(health.Value!.DriverStatus)
            : ConnectorValue<ConnectorDriverStatus>.Unknown(health.ErrorCode);
        return ConnectorValue<ScannerStatus>.Known(new ScannerStatus(
            OnlineStatus(device.OnlineStatus),
            driverStatus));
    }

    public override async Task<ConnectorValue<ScannerDiagnostics>> ReadDiagnosticsAsync(
        ScannerConnectionTarget target,
        CancellationToken cancellationToken)
    {
        var result = await ReadHealthReadingAsync(target, cancellationToken);
        return MapReading(result, reading => new ScannerDiagnostics(
            Number(reading.JamCount),
            Number(reading.DoubleFeedCount),
            Number(reading.TransportErrorCount),
            reading.MaintenanceCountersKnown
                ? ConnectorValue<ImmutableDictionary<string, long>>.Known(reading.MaintenanceCounters)
                : ConnectorValue<ImmutableDictionary<string, long>>.Unknown()));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _discoveryGate.Dispose();
        _healthGate.Dispose();
        _disposed = true;
    }

    protected static ConnectorDescriptor CreateDescriptor(
        string connectorId,
        string displayName,
        string protocol,
        bool developmentOnly,
        bool includesLogReferences = false)
    {
        var capabilities = ImmutableArray.CreateBuilder<ConnectorCapability>();
        capabilities.Add(ConnectorCapability.Discovery);
        capabilities.Add(ConnectorCapability.Identity);
        capabilities.Add(ConnectorCapability.Capabilities);
        capabilities.Add(ConnectorCapability.Firmware);
        capabilities.Add(ConnectorCapability.Counters);
        capabilities.Add(ConnectorCapability.Health);
        capabilities.Add(ConnectorCapability.CurrentStatus);
        capabilities.Add(ConnectorCapability.Diagnostics);
        if (includesLogReferences)
        {
            capabilities.Add(ConnectorCapability.LogReferences);
        }

        return new ConnectorDescriptor(
            connectorId,
            displayName,
            protocol,
            SupportedManufacturer: null,
            developmentOnly,
            capabilities.ToImmutable());
    }

    private async Task<ConnectorValue<ScannerHealthReading>> ReadHealthReadingAsync(
        ScannerConnectionTarget target,
        CancellationToken cancellationToken)
    {
        if (!TryGetDevice(target, out var device))
        {
            return ConnectorValue<ScannerHealthReading>.Failed(ConnectorErrorCodes.TargetNotFound);
        }

        await _healthGate.WaitAsync(cancellationToken);
        try
        {
            if (_healthResult is null)
            {
                try
                {
                    _healthResult = await _healthProvider.CollectAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    return ConnectorValue<ScannerHealthReading>.Failed();
                }
            }

            if (!_healthResult.IsAvailable)
            {
                return ConnectorValue<ScannerHealthReading>.Unavailable(_healthResult.ErrorCode);
            }

            if (!string.IsNullOrWhiteSpace(_healthResult.ErrorCode))
            {
                return ConnectorValue<ScannerHealthReading>.Failed(_healthResult.ErrorCode);
            }

            var reading = _healthResult.Readings.FirstOrDefault(candidate =>
                string.Equals(candidate.SourceId, device.SourceId, StringComparison.Ordinal));
            return reading is null
                ? ConnectorValue<ScannerHealthReading>.Unknown()
                : ConnectorValue<ScannerHealthReading>.Known(reading);
        }
        finally
        {
            _healthGate.Release();
        }
    }

    private static ConnectorValue<TOutput> MapReading<TOutput>(
        ConnectorValue<ScannerHealthReading> result,
        Func<ScannerHealthReading, TOutput> map)
        where TOutput : class =>
        result.State switch
        {
            ConnectorResultState.Known => ConnectorValue<TOutput>.Known(map(result.Value!)),
            ConnectorResultState.Unavailable => ConnectorValue<TOutput>.Unavailable(result.ErrorCode),
            ConnectorResultState.Failed => ConnectorValue<TOutput>.Failed(result.ErrorCode),
            _ => ConnectorValue<TOutput>.Unknown(result.ErrorCode)
        };

    private bool TryGetDevice(ScannerConnectionTarget target, out AdapterScannerDevice device)
    {
        if (!string.Equals(target.ConnectorId, Descriptor.ConnectorId, StringComparison.Ordinal))
        {
            device = null!;
            return false;
        }

        return _devices.TryGetValue(target.TargetId, out device!);
    }

    private static ConnectorValue<string> Text(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? ConnectorValue<string>.Unknown()
            : ConnectorValue<string>.Known(value.Trim());

    private static ConnectorValue<bool> Boolean(bool? value) =>
        value.HasValue ? ConnectorValue<bool>.Known(value.Value) : ConnectorValue<bool>.Unknown();

    private static ConnectorValue<long> Number(long? value) =>
        value.HasValue ? ConnectorValue<long>.Known(value.Value) : ConnectorValue<long>.Unknown();

    private static ConnectorValue<decimal> Number(decimal? value) =>
        value.HasValue ? ConnectorValue<decimal>.Known(value.Value) : ConnectorValue<decimal>.Unknown();

    private static ConnectorValue<DateTimeOffset> Date(DateTimeOffset? value) =>
        value.HasValue ? ConnectorValue<DateTimeOffset>.Known(value.Value) : ConnectorValue<DateTimeOffset>.Unknown();

    private static ConnectorValue<TimeSpan> Duration(TimeSpan? value) =>
        value.HasValue ? ConnectorValue<TimeSpan>.Known(value.Value) : ConnectorValue<TimeSpan>.Unknown();

    private static ConnectorValue<ConnectorScannerOnlineStatus> OnlineStatus(ScannerOnlineStatus value) =>
        value switch
        {
            ScannerOnlineStatus.Online => ConnectorValue<ConnectorScannerOnlineStatus>.Known(
                ConnectorScannerOnlineStatus.Online),
            ScannerOnlineStatus.Offline => ConnectorValue<ConnectorScannerOnlineStatus>.Known(
                ConnectorScannerOnlineStatus.Offline),
            _ => ConnectorValue<ConnectorScannerOnlineStatus>.Unknown()
        };

    private static ConnectorValue<ConnectorDriverStatus> DriverStatus(ScannerDriverHealthStatus value) =>
        value switch
        {
            ScannerDriverHealthStatus.Ready => ConnectorValue<ConnectorDriverStatus>.Known(ConnectorDriverStatus.Ready),
            ScannerDriverHealthStatus.Degraded => ConnectorValue<ConnectorDriverStatus>.Known(
                ConnectorDriverStatus.Degraded),
            ScannerDriverHealthStatus.Error => ConnectorValue<ConnectorDriverStatus>.Known(ConnectorDriverStatus.Error),
            _ => ConnectorValue<ConnectorDriverStatus>.Unknown()
        };

    private static string CreateTargetId(string connectorId, string sourceId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{connectorId}|{sourceId}"));
        return $"target-{Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant()}";
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
