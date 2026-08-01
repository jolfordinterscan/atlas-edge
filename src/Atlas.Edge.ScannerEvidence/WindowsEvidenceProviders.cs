using System.Collections.Immutable;

namespace Atlas.Edge.ScannerEvidence;

public interface IPlatformContext
{
    bool IsWindows { get; }
}

public sealed class SystemPlatformContext : IPlatformContext
{
    public bool IsWindows => OperatingSystem.IsWindows();
}

public sealed record WindowsEvidenceCatalogResult<T>(
    bool IsAvailable,
    ImmutableArray<T> Records,
    string? ErrorCode);

public sealed record WindowsDeviceEvidenceRecord(
    string RecordId,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string? HardwareInstanceId,
    string? StableUsbPath,
    string? UsbVendorId,
    string? UsbProductId,
    bool? Present,
    DateTimeOffset? LastArrivalUtc,
    DateTimeOffset? LastRemovalUtc);

public sealed record WindowsDriverEvidenceRecord(
    string RecordId,
    string? HardwareInstanceId,
    string? PackageName,
    string? Version,
    string? Provider,
    DateTimeOffset? DriverDate);

public sealed record WindowsServiceEvidenceRecord(
    string RecordId,
    string ServiceName,
    EvidenceServiceState? State,
    string? Version,
    string? AdministratorMappingId);

public sealed record WindowsEventEvidenceRecord(
    string RecordId,
    EvidenceEventKind Kind,
    string StableEventCode,
    DateTimeOffset? OccurredAtUtc,
    string? ReferenceId,
    string? HardwareInstanceId,
    string? StableUsbPath,
    string? AdministratorMappingId);

public sealed record WindowsRegistryEvidenceRecord(
    string RecordId,
    string RegistryPath,
    ImmutableDictionary<string, string> Values,
    string? HardwareInstanceId,
    string? AdministratorMappingId);

public interface IWindowsPnpEvidenceCatalog
{
    Task<WindowsEvidenceCatalogResult<WindowsDeviceEvidenceRecord>> ReadAsync(CancellationToken cancellationToken);
}

public interface IWindowsDriverEvidenceCatalog
{
    Task<WindowsEvidenceCatalogResult<WindowsDriverEvidenceRecord>> ReadAsync(CancellationToken cancellationToken);
}

public interface IWindowsServiceEvidenceCatalog
{
    Task<WindowsEvidenceCatalogResult<WindowsServiceEvidenceRecord>> ReadAsync(
        ImmutableArray<string> serviceNames,
        CancellationToken cancellationToken);
}

public interface IWindowsEventEvidenceCatalog
{
    Task<WindowsEvidenceCatalogResult<WindowsEventEvidenceRecord>> ReadAsync(
        ImmutableArray<string> channels,
        ImmutableArray<string> providers,
        CancellationToken cancellationToken);
}

public interface IWindowsRegistryEvidenceCatalog
{
    Task<WindowsEvidenceCatalogResult<WindowsRegistryEvidenceRecord>> ReadAsync(
        ImmutableArray<string> registryPaths,
        CancellationToken cancellationToken);
}

public sealed class UnavailableWindowsEvidenceCatalog :
    IWindowsPnpEvidenceCatalog,
    IWindowsDriverEvidenceCatalog,
    IWindowsServiceEvidenceCatalog,
    IWindowsEventEvidenceCatalog,
    IWindowsRegistryEvidenceCatalog
{
    private const string ErrorCode = "windows_reader_unavailable";

    public Task<WindowsEvidenceCatalogResult<WindowsDeviceEvidenceRecord>> ReadAsync(
        CancellationToken cancellationToken) => Unavailable<WindowsDeviceEvidenceRecord>(cancellationToken);

    Task<WindowsEvidenceCatalogResult<WindowsDriverEvidenceRecord>> IWindowsDriverEvidenceCatalog.ReadAsync(
        CancellationToken cancellationToken) => Unavailable<WindowsDriverEvidenceRecord>(cancellationToken);

    Task<WindowsEvidenceCatalogResult<WindowsServiceEvidenceRecord>> IWindowsServiceEvidenceCatalog.ReadAsync(
        ImmutableArray<string> serviceNames,
        CancellationToken cancellationToken) => Unavailable<WindowsServiceEvidenceRecord>(cancellationToken);

    public Task<WindowsEvidenceCatalogResult<WindowsEventEvidenceRecord>> ReadAsync(
        ImmutableArray<string> channels,
        ImmutableArray<string> providers,
        CancellationToken cancellationToken) => Unavailable<WindowsEventEvidenceRecord>(cancellationToken);

    Task<WindowsEvidenceCatalogResult<WindowsRegistryEvidenceRecord>> IWindowsRegistryEvidenceCatalog.ReadAsync(
        ImmutableArray<string> registryPaths,
        CancellationToken cancellationToken) => Unavailable<WindowsRegistryEvidenceRecord>(cancellationToken);

    private static Task<WindowsEvidenceCatalogResult<T>> Unavailable<T>(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new WindowsEvidenceCatalogResult<T>(false, ImmutableArray<T>.Empty, ErrorCode));
    }
}

public sealed class WindowsPnpEvidenceProvider : ScannerEvidenceProviderBase
{
    private readonly IWindowsPnpEvidenceCatalog _catalog;
    private readonly IPlatformContext _platform;
    private ImmutableDictionary<string, WindowsDeviceEvidenceRecord> _records =
        ImmutableDictionary<string, WindowsDeviceEvidenceRecord>.Empty;

    public WindowsPnpEvidenceProvider(IWindowsPnpEvidenceCatalog catalog, IPlatformContext platform)
    {
        _catalog = catalog;
        _platform = platform;
    }

    public override EvidenceSourceDescriptor Descriptor { get; } = DescriptorFactory.Create(
        "windows_pnp",
        "Windows Plug and Play Evidence",
        EvidenceSourceQuality.OperatingSystem,
        EvidenceCapability.DeviceIdentity,
        EvidenceCapability.Connection);

    public override Task<EvidenceAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken) =>
        WindowsProvider.ReadAvailabilityAsync(_platform, LoadAsync, cancellationToken);

    public override async Task<EvidenceValue<ImmutableArray<ScannerEvidenceTarget>>> DiscoverTargetsAsync(
        CancellationToken cancellationToken)
    {
        if (!_platform.IsWindows)
        {
            return EvidenceValue<ImmutableArray<ScannerEvidenceTarget>>.Unavailable(
                EvidenceErrorCodes.PlatformUnavailable);
        }

        var result = await LoadAsync(cancellationToken);
        if (!result.IsAvailable)
        {
            return EvidenceValue<ImmutableArray<ScannerEvidenceTarget>>.Unavailable(result.ErrorCode);
        }

        _records = result.Records.ToImmutableDictionary(
            record => TargetId(record.RecordId),
            StringComparer.Ordinal);
        return EvidenceValue<ImmutableArray<ScannerEvidenceTarget>>.Known(
            _records.Select(item => new ScannerEvidenceTarget(
                item.Key,
                Descriptor.ProviderId,
                Correlations(item.Value))).ToImmutableArray());
    }

    public override Task<EvidenceValue<DeviceIdentityEvidence>> ReadIdentityAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_records.TryGetValue(target.TargetId, out var record)
            ? EvidenceValue<DeviceIdentityEvidence>.Known(new DeviceIdentityEvidence(
                Values.Text(record.Manufacturer),
                Values.Text(record.Model),
                Values.Text(record.SerialNumber),
                Values.Text(record.HardwareInstanceId),
                Values.Text(record.UsbVendorId),
                Values.Text(record.UsbProductId)))
            : EvidenceValue<DeviceIdentityEvidence>.Failed(EvidenceErrorCodes.TargetNotFound));
    }

    public override Task<EvidenceValue<ConnectionEvidence>> ReadConnectionAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_records.TryGetValue(target.TargetId, out var record)
            ? EvidenceValue<ConnectionEvidence>.Known(new ConnectionEvidence(
                Values.Boolean(record.Present),
                Values.Text(record.StableUsbPath),
                Values.Date(record.LastArrivalUtc),
                Values.Date(record.LastRemovalUtc)))
            : EvidenceValue<ConnectionEvidence>.Failed(EvidenceErrorCodes.TargetNotFound));
    }

    private Task<WindowsEvidenceCatalogResult<WindowsDeviceEvidenceRecord>> LoadAsync(
        CancellationToken cancellationToken) => _catalog.ReadAsync(cancellationToken);

    private static string TargetId(string recordId) => "pnp-" + EvidenceIdentity.Hash("pnp_target", recordId);

    private static ImmutableArray<EvidenceCorrelationKey> Correlations(WindowsDeviceEvidenceRecord record)
    {
        var builder = ImmutableArray.CreateBuilder<EvidenceCorrelationKey>();
        Correlation.Add(builder, EvidenceCorrelationKind.HardwareInstance, "hardware", record.HardwareInstanceId);
        Correlation.Add(builder, EvidenceCorrelationKind.StableUsbPath, "usb_path", record.StableUsbPath);
        Correlation.AddManufacturerSerial(builder, record.Manufacturer, record.SerialNumber);
        return builder.ToImmutable();
    }
}

public sealed class WindowsDriverEvidenceProvider : ScannerEvidenceProviderBase
{
    private readonly IWindowsDriverEvidenceCatalog _catalog;
    private readonly IPlatformContext _platform;
    private ImmutableDictionary<string, WindowsDriverEvidenceRecord> _records =
        ImmutableDictionary<string, WindowsDriverEvidenceRecord>.Empty;

    public WindowsDriverEvidenceProvider(IWindowsDriverEvidenceCatalog catalog, IPlatformContext platform)
    {
        _catalog = catalog;
        _platform = platform;
    }

    public override EvidenceSourceDescriptor Descriptor { get; } = DescriptorFactory.Create(
        "windows_driver",
        "Windows Driver Evidence",
        EvidenceSourceQuality.OperatingSystem,
        EvidenceCapability.Driver);

    public override Task<EvidenceAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken) =>
        WindowsProvider.ReadAvailabilityAsync(_platform, LoadAsync, cancellationToken);

    public override async Task<EvidenceValue<ImmutableArray<ScannerEvidenceTarget>>> DiscoverTargetsAsync(
        CancellationToken cancellationToken)
    {
        if (!_platform.IsWindows)
        {
            return EvidenceValue<ImmutableArray<ScannerEvidenceTarget>>.Unavailable(
                EvidenceErrorCodes.PlatformUnavailable);
        }

        var result = await LoadAsync(cancellationToken);
        if (!result.IsAvailable)
        {
            return EvidenceValue<ImmutableArray<ScannerEvidenceTarget>>.Unavailable(result.ErrorCode);
        }

        _records = result.Records.ToImmutableDictionary(
            record => "driver-" + EvidenceIdentity.Hash("driver_target", record.RecordId),
            StringComparer.Ordinal);
        return EvidenceValue<ImmutableArray<ScannerEvidenceTarget>>.Known(_records.Select(item =>
        {
            var correlations = ImmutableArray.CreateBuilder<EvidenceCorrelationKey>();
            Correlation.Add(correlations, EvidenceCorrelationKind.HardwareInstance, "hardware", item.Value.HardwareInstanceId);
            return new ScannerEvidenceTarget(item.Key, Descriptor.ProviderId, correlations.ToImmutable());
        }).ToImmutableArray());
    }

    public override Task<EvidenceValue<DriverEvidence>> ReadDriverAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_records.TryGetValue(target.TargetId, out var record)
            ? EvidenceValue<DriverEvidence>.Known(new DriverEvidence(
                Values.Text(record.PackageName),
                Values.Text(record.Version),
                Values.Text(record.Provider),
                Values.Date(record.DriverDate)))
            : EvidenceValue<DriverEvidence>.Failed(EvidenceErrorCodes.TargetNotFound));
    }

    private Task<WindowsEvidenceCatalogResult<WindowsDriverEvidenceRecord>> LoadAsync(
        CancellationToken cancellationToken) => _catalog.ReadAsync(cancellationToken);
}

public sealed class WindowsServiceEvidenceProvider : WindowsListEvidenceProvider<WindowsServiceEvidenceRecord>
{
    private readonly IWindowsServiceEvidenceCatalog _catalog;
    private readonly ImmutableArray<string> _serviceNames;

    public WindowsServiceEvidenceProvider(
        IWindowsServiceEvidenceCatalog catalog,
        IPlatformContext platform,
        IEnumerable<string> serviceNames)
        : base(
            DescriptorFactory.Create(
                "windows_service",
                "Windows Service Evidence",
                EvidenceSourceQuality.OperatingSystem,
                EvidenceCapability.Services),
            platform)
    {
        _catalog = catalog;
        _serviceNames = serviceNames.ToImmutableArray();
        if (_serviceNames.Any(name => !EvidenceSafetyPolicy.IsSafeAllowlistName(name)))
        {
            throw new ArgumentException("Windows service evidence requires explicit allowlisted names.");
        }
    }

    protected override Task<WindowsEvidenceCatalogResult<WindowsServiceEvidenceRecord>> LoadAsync(
        CancellationToken cancellationToken) => _catalog.ReadAsync(_serviceNames, cancellationToken);

    protected override string RecordId(WindowsServiceEvidenceRecord record) => record.RecordId;

    protected override ImmutableArray<EvidenceCorrelationKey> Correlations(WindowsServiceEvidenceRecord record)
    {
        var builder = ImmutableArray.CreateBuilder<EvidenceCorrelationKey>();
        Correlation.Add(
            builder,
            EvidenceCorrelationKind.AdministratorMapping,
            "administrator_mapping",
            record.AdministratorMappingId);
        return builder.ToImmutable();
    }

    public override Task<EvidenceValue<ImmutableArray<ServiceEvidence>>> ReadServicesAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken) =>
        MapRecord(target, cancellationToken, record => ImmutableArray.Create(new ServiceEvidence(
            record.ServiceName,
            record.State.HasValue
                ? EvidenceValue<EvidenceServiceState>.Known(record.State.Value)
                : EvidenceValue<EvidenceServiceState>.Unknown(),
            Values.Text(record.Version))));
}

public sealed class WindowsEventLogEvidenceProvider : WindowsListEvidenceProvider<WindowsEventEvidenceRecord>
{
    private readonly IWindowsEventEvidenceCatalog _catalog;
    private readonly ImmutableArray<string> _channels;
    private readonly ImmutableArray<string> _providers;

    public WindowsEventLogEvidenceProvider(
        IWindowsEventEvidenceCatalog catalog,
        IPlatformContext platform,
        IEnumerable<string> channels,
        IEnumerable<string> providers)
        : base(
            DescriptorFactory.Create(
                "windows_event_log",
                "Windows Event Log Evidence",
                EvidenceSourceQuality.OperatingSystem,
                EvidenceCapability.Events),
            platform)
    {
        _catalog = catalog;
        _channels = channels.ToImmutableArray();
        _providers = providers.ToImmutableArray();
        if (_channels.Any(channel => !EvidenceSafetyPolicy.IsSafeAllowlistName(channel)) ||
            _providers.Any(provider => !EvidenceSafetyPolicy.IsSafeAllowlistName(provider)))
        {
            throw new ArgumentException("Windows Event Log evidence requires explicit channel and provider allowlists.");
        }
    }

    protected override Task<WindowsEvidenceCatalogResult<WindowsEventEvidenceRecord>> LoadAsync(
        CancellationToken cancellationToken) => _catalog.ReadAsync(_channels, _providers, cancellationToken);

    protected override string RecordId(WindowsEventEvidenceRecord record) => record.RecordId;

    protected override ImmutableArray<EvidenceCorrelationKey> Correlations(WindowsEventEvidenceRecord record)
    {
        var builder = ImmutableArray.CreateBuilder<EvidenceCorrelationKey>();
        Correlation.Add(builder, EvidenceCorrelationKind.HardwareInstance, "hardware", record.HardwareInstanceId);
        Correlation.Add(builder, EvidenceCorrelationKind.StableUsbPath, "usb_path", record.StableUsbPath);
        Correlation.Add(
            builder,
            EvidenceCorrelationKind.AdministratorMapping,
            "administrator_mapping",
            record.AdministratorMappingId);
        return builder.ToImmutable();
    }

    public override Task<EvidenceValue<ImmutableArray<EventEvidence>>> ReadEventsAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken) =>
        MapRecord(target, cancellationToken, record => ImmutableArray.Create(new EventEvidence(
            record.Kind,
            EvidenceErrorCodes.Normalize(record.StableEventCode, EvidenceErrorCodes.DataUnknown),
            Values.Date(record.OccurredAtUtc),
            Values.Text(record.ReferenceId))));
}

public sealed class WindowsRegistryEvidenceProvider : WindowsListEvidenceProvider<WindowsRegistryEvidenceRecord>
{
    private readonly IWindowsRegistryEvidenceCatalog _catalog;
    private readonly ImmutableArray<string> _paths;

    public WindowsRegistryEvidenceProvider(
        IWindowsRegistryEvidenceCatalog catalog,
        IPlatformContext platform,
        IEnumerable<string> paths)
        : base(
            DescriptorFactory.Create(
                "windows_registry",
                "Allowlisted Windows Registry Evidence",
                EvidenceSourceQuality.OperatingSystem,
                EvidenceCapability.Maintenance),
            platform)
    {
        _catalog = catalog;
        _paths = paths.ToImmutableArray();
        if (_paths.Any(path => !EvidenceSafetyPolicy.IsSafeRegistryPath(path)))
        {
            throw new ArgumentException("Windows registry evidence requires explicit allowlisted HKLM subkeys.");
        }
    }

    protected override Task<WindowsEvidenceCatalogResult<WindowsRegistryEvidenceRecord>> LoadAsync(
        CancellationToken cancellationToken) => _catalog.ReadAsync(_paths, cancellationToken);

    protected override string RecordId(WindowsRegistryEvidenceRecord record) => record.RecordId;

    protected override ImmutableArray<EvidenceCorrelationKey> Correlations(WindowsRegistryEvidenceRecord record)
    {
        var builder = ImmutableArray.CreateBuilder<EvidenceCorrelationKey>();
        Correlation.Add(builder, EvidenceCorrelationKind.HardwareInstance, "hardware", record.HardwareInstanceId);
        Correlation.Add(
            builder,
            EvidenceCorrelationKind.AdministratorMapping,
            "administrator_mapping",
            record.AdministratorMappingId);
        return builder.ToImmutable();
    }

    public override Task<EvidenceValue<MaintenanceEvidence>> ReadMaintenanceAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken) =>
        MapRecord(target, cancellationToken, record => new MaintenanceEvidence(
            record.Values.ToImmutableDictionary(
                item => item.Key,
                item => Values.Text(item.Value),
                StringComparer.OrdinalIgnoreCase)));
}

public abstract class WindowsListEvidenceProvider<TRecord> : ScannerEvidenceProviderBase
    where TRecord : class
{
    private readonly IPlatformContext _platform;
    private ImmutableDictionary<string, TRecord> _records = ImmutableDictionary<string, TRecord>.Empty;

    protected WindowsListEvidenceProvider(EvidenceSourceDescriptor descriptor, IPlatformContext platform)
    {
        Descriptor = descriptor;
        _platform = platform;
    }

    public override EvidenceSourceDescriptor Descriptor { get; }

    public override Task<EvidenceAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken) =>
        WindowsProvider.ReadAvailabilityAsync(_platform, LoadAsync, cancellationToken);

    public override async Task<EvidenceValue<ImmutableArray<ScannerEvidenceTarget>>> DiscoverTargetsAsync(
        CancellationToken cancellationToken)
    {
        if (!_platform.IsWindows)
        {
            return EvidenceValue<ImmutableArray<ScannerEvidenceTarget>>.Unavailable(
                EvidenceErrorCodes.PlatformUnavailable);
        }

        var result = await LoadAsync(cancellationToken);
        if (!result.IsAvailable)
        {
            return EvidenceValue<ImmutableArray<ScannerEvidenceTarget>>.Unavailable(result.ErrorCode);
        }

        _records = result.Records.ToImmutableDictionary(
            record => "windows-" + EvidenceIdentity.Hash(Descriptor.ProviderId, RecordId(record)),
            StringComparer.Ordinal);
        return EvidenceValue<ImmutableArray<ScannerEvidenceTarget>>.Known(_records.Select(item =>
            new ScannerEvidenceTarget(item.Key, Descriptor.ProviderId, Correlations(item.Value))).ToImmutableArray());
    }

    protected abstract Task<WindowsEvidenceCatalogResult<TRecord>> LoadAsync(CancellationToken cancellationToken);

    protected abstract string RecordId(TRecord record);

    protected abstract ImmutableArray<EvidenceCorrelationKey> Correlations(TRecord record);

    protected Task<EvidenceValue<TOutput>> MapRecord<TOutput>(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken,
        Func<TRecord, TOutput> map)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_records.TryGetValue(target.TargetId, out var record)
            ? EvidenceValue<TOutput>.Known(map(record))
            : EvidenceValue<TOutput>.Failed(EvidenceErrorCodes.TargetNotFound));
    }
}

internal static class WindowsProvider
{
    public static async Task<EvidenceAvailability> ReadAvailabilityAsync<T>(
        IPlatformContext platform,
        Func<CancellationToken, Task<WindowsEvidenceCatalogResult<T>>> load,
        CancellationToken cancellationToken)
    {
        if (!platform.IsWindows)
        {
            return EvidenceAvailability.Unavailable(EvidenceErrorCodes.PlatformUnavailable);
        }

        try
        {
            var result = await load(cancellationToken);
            return result.IsAvailable
                ? EvidenceAvailability.Available()
                : EvidenceAvailability.Unavailable(result.ErrorCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return EvidenceAvailability.Failed();
        }
    }
}

internal static class DescriptorFactory
{
    public static EvidenceSourceDescriptor Create(
        string providerId,
        string displayName,
        EvidenceSourceQuality quality,
        params EvidenceCapability[] capabilities) =>
        new(
            providerId,
            displayName,
            "Windows",
            quality,
            false,
            capabilities.Prepend(EvidenceCapability.Discovery).Distinct().ToImmutableArray());
}

internal static class Correlation
{
    public static void Add(
        ImmutableArray<EvidenceCorrelationKey>.Builder builder,
        EvidenceCorrelationKind kind,
        string purpose,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.Add(new EvidenceCorrelationKey(kind, EvidenceIdentity.Hash(purpose, value)));
        }
    }

    public static void AddManufacturerSerial(
        ImmutableArray<EvidenceCorrelationKey>.Builder builder,
        string? manufacturer,
        string? serialNumber)
    {
        if (!string.IsNullOrWhiteSpace(manufacturer) && !string.IsNullOrWhiteSpace(serialNumber))
        {
            builder.Add(new EvidenceCorrelationKey(
                EvidenceCorrelationKind.ManufacturerSerial,
                EvidenceIdentity.Hash("manufacturer_serial", manufacturer, serialNumber)));
        }
    }
}

internal static class Values
{
    public static EvidenceValue<string> Text(string? value) =>
        string.IsNullOrWhiteSpace(value) ? EvidenceValue<string>.Unknown() : EvidenceValue<string>.Known(value.Trim());

    public static EvidenceValue<bool> Boolean(bool? value) =>
        value.HasValue ? EvidenceValue<bool>.Known(value.Value) : EvidenceValue<bool>.Unknown();

    public static EvidenceValue<DateTimeOffset> Date(DateTimeOffset? value) =>
        value.HasValue ? EvidenceValue<DateTimeOffset>.Known(value.Value.ToUniversalTime()) : EvidenceValue<DateTimeOffset>.Unknown();
}
