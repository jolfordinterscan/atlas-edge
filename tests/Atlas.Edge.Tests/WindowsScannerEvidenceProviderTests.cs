using System.Collections.Immutable;
using Atlas.Edge.ScannerEvidence;

namespace Atlas.Edge.Tests;

public sealed class WindowsScannerEvidenceProviderTests
{
    [Fact]
    public async Task WindowsProviders_ReportUnavailableWithoutReadingCatalogsOnNonWindows()
    {
        var catalogs = new FakeWindowsCatalogs { ThrowIfCalled = true };
        var platform = new TestPlatform(isWindows: false);
        IScannerEvidenceProvider[] providers =
        [
            new WindowsPnpEvidenceProvider(catalogs, platform),
            new WindowsDriverEvidenceProvider(catalogs, platform),
            new WindowsServiceEvidenceProvider(catalogs, platform, ["stisvc"]),
            new WindowsEventLogEvidenceProvider(catalogs, platform, ["System"], ["Service Control Manager"]),
            new WindowsRegistryEvidenceProvider(catalogs, platform, [@"HKLM\SOFTWARE\Vendor\Scanner"])
        ];

        foreach (var provider in providers)
        {
            var availability = await provider.CheckAvailabilityAsync(CancellationToken.None);
            Assert.Equal(EvidenceValueState.Unavailable, availability.State);
            Assert.Equal(EvidenceErrorCodes.PlatformUnavailable, availability.ErrorCode);
            var discovery = await provider.DiscoverTargetsAsync(CancellationToken.None);
            Assert.Equal(EvidenceValueState.Unavailable, discovery.State);
            Assert.Equal(EvidenceErrorCodes.PlatformUnavailable, discovery.ErrorCode);
        }

        Assert.Equal(0, catalogs.Calls);
    }

    [Fact]
    public async Task PnpProvider_NormalizesIdentityConnectionAndStrongCorrelations()
    {
        var catalogs = new FakeWindowsCatalogs
        {
            Devices = [new WindowsDeviceEvidenceRecord(
                "record-1",
                "Acme",
                "ScanPro",
                "SERIAL-1",
                @"USB\VID_1234&PID_5678\SERIAL-1",
                "USB-PORT-4",
                "1234",
                "5678",
                true,
                new DateTimeOffset(2026, 8, 1, 8, 0, 0, TimeSpan.Zero),
                null)]
        };
        var provider = new WindowsPnpEvidenceProvider(catalogs, new TestPlatform(true));

        Assert.Equal(EvidenceValueState.Known, (await provider.CheckAvailabilityAsync(CancellationToken.None)).State);
        var target = Assert.Single((await provider.DiscoverTargetsAsync(CancellationToken.None)).Value);
        var identity = (await provider.ReadIdentityAsync(target, CancellationToken.None)).Value;
        var connection = (await provider.ReadConnectionAsync(target, CancellationToken.None)).Value;

        Assert.Equal("Acme", identity.Manufacturer.Value);
        Assert.Equal("1234", identity.UsbVendorId.Value);
        Assert.True(connection.Present.Value);
        Assert.Equal(EvidenceValueState.Unknown, connection.LastRemovalUtc.State);
        Assert.Contains(target.CorrelationKeys, key => key.Kind == EvidenceCorrelationKind.HardwareInstance);
        Assert.Contains(target.CorrelationKeys, key => key.Kind == EvidenceCorrelationKind.StableUsbPath);
        Assert.DoesNotContain("SERIAL-1", target.TargetId, StringComparison.Ordinal);
        Assert.DoesNotContain("VID_1234", target.TargetId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DriverProvider_NormalizesMissingFieldsAsUnknown()
    {
        var catalogs = new FakeWindowsCatalogs
        {
            Drivers = [new WindowsDriverEvidenceRecord(
                "driver-1",
                "hardware-1",
                "scanner.inf",
                "4.2.0",
                "Acme",
                null)]
        };
        var provider = new WindowsDriverEvidenceProvider(catalogs, new TestPlatform(true));
        await provider.CheckAvailabilityAsync(CancellationToken.None);
        var target = Assert.Single((await provider.DiscoverTargetsAsync(CancellationToken.None)).Value);

        var driver = (await provider.ReadDriverAsync(target, CancellationToken.None)).Value;

        Assert.Equal("scanner.inf", driver.PackageName.Value);
        Assert.Equal("4.2.0", driver.Version.Value);
        Assert.Equal(EvidenceValueState.Unknown, driver.DriverDate.State);
    }

    [Fact]
    public async Task ServiceEventAndRegistryProviders_NormalizeAllowlistedCatalogRecords()
    {
        var catalogs = new FakeWindowsCatalogs
        {
            Services = [new WindowsServiceEvidenceRecord("service-1", "stisvc", EvidenceServiceState.Running, null, "map-1")],
            Events = [new WindowsEventEvidenceRecord(
                "event-1",
                EvidenceEventKind.UsbControllerReset,
                "usb_reset",
                DateTimeOffset.UtcNow,
                "event-reference-1",
                "hardware-1",
                null,
                null)],
            Registry = [new WindowsRegistryEvidenceRecord(
                "registry-1",
                @"HKLM\SOFTWARE\Vendor\Scanner",
                ImmutableDictionary<string, string>.Empty.Add("UtilityVersion", "3.1"),
                "hardware-1",
                null)]
        };
        var platform = new TestPlatform(true);
        var serviceProvider = new WindowsServiceEvidenceProvider(catalogs, platform, ["stisvc"]);
        var eventProvider = new WindowsEventLogEvidenceProvider(catalogs, platform, ["System"], ["USB"]);
        var registryProvider = new WindowsRegistryEvidenceProvider(
            catalogs,
            platform,
            [@"HKLM\SOFTWARE\Vendor\Scanner"]);

        await serviceProvider.CheckAvailabilityAsync(CancellationToken.None);
        await eventProvider.CheckAvailabilityAsync(CancellationToken.None);
        await registryProvider.CheckAvailabilityAsync(CancellationToken.None);
        var serviceTarget = Assert.Single((await serviceProvider.DiscoverTargetsAsync(CancellationToken.None)).Value);
        var eventTarget = Assert.Single((await eventProvider.DiscoverTargetsAsync(CancellationToken.None)).Value);
        var registryTarget = Assert.Single((await registryProvider.DiscoverTargetsAsync(CancellationToken.None)).Value);

        var service = Assert.Single((await serviceProvider.ReadServicesAsync(serviceTarget, CancellationToken.None)).Value);
        var eventItem = Assert.Single((await eventProvider.ReadEventsAsync(eventTarget, CancellationToken.None)).Value);
        var registry = (await registryProvider.ReadMaintenanceAsync(registryTarget, CancellationToken.None)).Value;

        Assert.Equal(EvidenceServiceState.Running, service.State.Value);
        Assert.Equal(EvidenceValueState.Unknown, service.Version.State);
        Assert.Equal("usb_reset", eventItem.StableEventCode);
        Assert.Equal("3.1", registry.Values["UtilityVersion"].Value);
    }

    private sealed class TestPlatform : IPlatformContext
    {
        public TestPlatform(bool isWindows) => IsWindows = isWindows;

        public bool IsWindows { get; }
    }

    private sealed class FakeWindowsCatalogs :
        IWindowsPnpEvidenceCatalog,
        IWindowsDriverEvidenceCatalog,
        IWindowsServiceEvidenceCatalog,
        IWindowsEventEvidenceCatalog,
        IWindowsRegistryEvidenceCatalog
    {
        public ImmutableArray<WindowsDeviceEvidenceRecord> Devices { get; init; } = [];

        public ImmutableArray<WindowsDriverEvidenceRecord> Drivers { get; init; } = [];

        public ImmutableArray<WindowsServiceEvidenceRecord> Services { get; init; } = [];

        public ImmutableArray<WindowsEventEvidenceRecord> Events { get; init; } = [];

        public ImmutableArray<WindowsRegistryEvidenceRecord> Registry { get; init; } = [];

        public bool ThrowIfCalled { get; init; }

        public int Calls { get; private set; }

        public Task<WindowsEvidenceCatalogResult<WindowsDeviceEvidenceRecord>> ReadAsync(
            CancellationToken cancellationToken) => Result(Devices);

        Task<WindowsEvidenceCatalogResult<WindowsDriverEvidenceRecord>> IWindowsDriverEvidenceCatalog.ReadAsync(
            CancellationToken cancellationToken) => Result(Drivers);

        public Task<WindowsEvidenceCatalogResult<WindowsServiceEvidenceRecord>> ReadAsync(
            ImmutableArray<string> serviceNames,
            CancellationToken cancellationToken) => Result(Services);

        public Task<WindowsEvidenceCatalogResult<WindowsEventEvidenceRecord>> ReadAsync(
            ImmutableArray<string> channels,
            ImmutableArray<string> providers,
            CancellationToken cancellationToken) => Result(Events);

        Task<WindowsEvidenceCatalogResult<WindowsRegistryEvidenceRecord>> IWindowsRegistryEvidenceCatalog.ReadAsync(
            ImmutableArray<string> registryPaths,
            CancellationToken cancellationToken) => Result(Registry);

        private Task<WindowsEvidenceCatalogResult<T>> Result<T>(ImmutableArray<T> records)
        {
            Calls++;
            if (ThrowIfCalled)
            {
                throw new InvalidOperationException("Catalog should not be called.");
            }

            return Task.FromResult(new WindowsEvidenceCatalogResult<T>(true, records, null));
        }
    }
}
