using Atlas.Edge.ScannerDiscovery;
using Atlas.Edge.Core;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace Atlas.Edge.Tests;

public sealed class WindowsScannerMetadataTests
{
    private static readonly WindowsScannerMetadataRecord FujitsuPnp = new(
        @"USB\VID_04C5&PID_15E0\SERIAL8170",
        [@"USB\VID_04C5&PID_15E0&REV_0100"],
        "{container-guid}",
        ["PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(3)"],
        "FUJITSU",
        "FUJITSU fi-8170",
        "fi Series WIA Driver",
        "PFU Limited",
        "2.1.0.4")
    {
        FirmwareVersion = "1.7"
    };

    [Fact]
    public async Task PnpProvider_EnrichesSerialDriverUsbAndHashedIdentifiers()
    {
        var provider = new WindowsPnpScannerMetadataProvider(new PnpCatalog(true, [FujitsuPnp]));

        var result = await provider.GetMetadataAsync(WiaScanner(), CancellationToken.None);

        Assert.NotNull(result.Metadata);
        Assert.Equal("SERIAL8170", result.Metadata.SerialNumber);
        Assert.Equal("WindowsPnPDeviceInstance", result.Metadata.SerialSource);
        Assert.Equal("04C5", result.Metadata.UsbVendorId);
        Assert.Equal("15E0", result.Metadata.UsbProductId);
        Assert.Equal("2.1.0.4", result.Metadata.DriverVersion);
        Assert.Equal("1.7", result.Metadata.FirmwareVersion);
        Assert.Equal("PFU Limited", result.Metadata.DriverProvider);
        Assert.Equal("FUJITSU fi-8170", result.Metadata.FriendlyName);
        Assert.All(
            [result.Metadata.HardwareId, result.Metadata.ContainerId, result.Metadata.LocationPathHash, result.Metadata.DeviceInstanceIdHash],
            value => Assert.Matches("^[a-f0-9]{64}$", value!));
        Assert.DoesNotContain("VID_04C5", string.Join('|', result.Metadata.HardwareId, result.Metadata.DeviceInstanceIdHash));
    }

    [Fact]
    public async Task SerialAbsent_RemainsUnknownWithoutInference()
    {
        var record = FujitsuPnp with { DeviceInstanceId = @"USB\VID_04C5&PID_15E0\6&ABC&0&3" };
        var result = await new WindowsPnpScannerMetadataProvider(new PnpCatalog(true, [record]))
            .GetMetadataAsync(WiaScanner(), CancellationToken.None);

        Assert.Null(result.Metadata!.SerialNumber);
        Assert.Null(result.Metadata.SerialSource);
    }

    [Fact]
    public void DriverUnavailable_RemainsUnknown()
    {
        var metadata = WindowsPnpScannerMetadataProvider.Normalize(FujitsuPnp with
        {
            DriverName = null,
            DriverProvider = null,
            DriverVersion = null
        });

        Assert.Null(metadata.DriverName);
        Assert.Null(metadata.DriverProvider);
        Assert.Null(metadata.DriverVersion);
    }

    [Fact]
    public async Task PnpAndRegistryUnavailable_ReturnUnavailableCleanly()
    {
        var pnp = await new WindowsPnpScannerMetadataProvider(new PnpCatalog(false, []))
            .GetMetadataAsync(WiaScanner(), CancellationToken.None);
        var registry = await new WindowsRegistryScannerMetadataProvider(new RegistryCatalog(false, []))
            .GetMetadataAsync(WiaScanner(), CancellationToken.None);

        Assert.False(pnp.IsAvailable);
        Assert.False(registry.IsAvailable);
        Assert.Null(pnp.Metadata);
        Assert.Null(registry.Metadata);
    }

    [Fact]
    public async Task DuplicateEqualMatches_AreNotCorrelated()
    {
        var result = await new WindowsPnpScannerMetadataProvider(
            new PnpCatalog(true, [FujitsuPnp, FujitsuPnp]))
            .GetMetadataAsync(WiaScanner(), CancellationToken.None);

        Assert.Null(result.Metadata);
    }

    [Fact]
    public void Hash_IsStableNormalizedSha256()
    {
        var first = WindowsPnpScannerMetadataProvider.Hash(" usb\\device ");
        var second = WindowsPnpScannerMetadataProvider.Hash("USB\\DEVICE");

        Assert.Equal(first, second);
        Assert.Matches("^[a-f0-9]{64}$", first!);
        Assert.Equal("****8170", ScannerMetadataPrivacy.MaskSerial("SERIAL8170"));
        Assert.Equal("Unknown", ScannerMetadataPrivacy.MaskSerial(null));
    }

    [Fact]
    public async Task Enricher_IsolatesFailureAndTimeoutThenUsesAvailableProvider()
    {
        var enricher = new ScannerMetadataEnricher(
            [new ThrowingProvider(), new SlowProvider(), new StaticProvider()],
            TimeProvider.System,
            TimeSpan.FromMilliseconds(20));

        var result = await enricher.EnrichAsync([WiaScanner()], CancellationToken.None);

        Assert.Equal("SERIAL8170", result[0].SerialNumber);
        Assert.Equal("3.2.1", result[0].Driver.Version);
    }

    [Fact]
    public async Task NonWindowsCatalogs_DoNotFakeWindowsMetadata()
    {
        if (OperatingSystem.IsWindows()) return;

        Assert.False((await new WindowsPnpScannerMetadataCatalog().ReadAsync(CancellationToken.None)).IsAvailable);
        Assert.False((await new WindowsScannerRegistryMetadataCatalog().ReadAsync(CancellationToken.None)).IsAvailable);
    }

    [Fact]
    public async Task EnrichedMetadata_FlowsToNormalizedInventoryWithoutRawIdentifiers()
    {
        var metadata = WindowsPnpScannerMetadataProvider.Normalize(FujitsuPnp);
        var enricher = new ScannerMetadataEnricher(
            [new MetadataProvider(metadata)], TimeProvider.System, TimeSpan.FromSeconds(1));
        var service = new ScannerDiscoveryService(
            [new Adapter(WiaScanner())],
            TimeProvider.System,
            NullLogger<ScannerDiscoveryService>.Instance,
            new ScannerIdentityFactory(),
            TimeSpan.FromSeconds(1),
            enricher);

        var snapshot = await service.DiscoverAsync(CancellationToken.None);
        var scanner = Assert.Single(snapshot.Scanners);
        Assert.Equal("SERIAL8170", scanner.SerialNumber);
        Assert.Equal("2.1.0.4", scanner.Drivers[0].Version);
        Assert.Equal("1.7", scanner.FirmwareVersion);
        Assert.Equal("04C5", scanner.UsbVendorId);
        var inventory = new ScannerInventoryEventBuilder().Build(
            snapshot,
            new AgentIdentity("agent", "workstation", "tenant", "Test", false, DateTimeOffset.UtcNow));
        var entry = Assert.Single(inventory.Scanners);
        Assert.Equal(metadata.DeviceInstanceIdHash, entry.DeviceInstanceIdHash);
        var json = JsonSerializer.Serialize(inventory);
        Assert.DoesNotContain(FujitsuPnp.DeviceInstanceId, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(FujitsuPnp.LocationPaths[0], json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(StillImageRegistryPathFragment, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WindowsMetadataSource_IsReadOnlyAndHasNoAcquisitionOrCommandSurface()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var source = File.ReadAllText(Path.Combine(root, "src/Atlas.Edge.ScannerDiscovery/WindowsScannerMetadata.cs"));

        Assert.Contains("writable: false", source, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "SetValue(", "CreateSubKey", "DeleteSubKey", "Transfer(", "ShowAcquireImage", "ShowSelectDevice", "scanner_command", "remote_control" })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static AdapterScannerDevice WiaScanner() => new(
        @"USB\VID_04C5&PID_15E0\SERIAL8170",
        ScannerProtocol.Wia,
        "FUJITSU",
        "fi-8170",
        null,
        null,
        "USB",
        null,
        null,
        null,
        [],
        new ScannerDriver("fi-8170", null, null),
        ScannerOnlineStatus.Unknown)
    {
        DevicePath = @"USB\VID_04C5&PID_15E0\SERIAL8170"
    };

    private const string StillImageRegistryPathFragment = @"SYSTEM\CurrentControlSet\Control\StillImage";

    private sealed class Adapter(AdapterScannerDevice scanner) : IScannerDiscoveryAdapter
    {
        public ScannerProtocol Protocol => ScannerProtocol.Wia;
        public Task<ScannerAdapterResult> DiscoverAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ScannerAdapterResult.Available(Protocol, [scanner]));
    }

    private sealed class PnpCatalog(bool available, IReadOnlyList<WindowsScannerMetadataRecord> records) : IPnpScannerMetadataCatalog
    {
        public Task<(bool IsAvailable, IReadOnlyList<WindowsScannerMetadataRecord> Records)> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult((available, records));
    }

    private sealed class RegistryCatalog(bool available, IReadOnlyList<WindowsScannerMetadataRecord> records) : IRegistryScannerMetadataCatalog
    {
        public Task<(bool IsAvailable, IReadOnlyList<WindowsScannerMetadataRecord> Records)> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult((available, records));
    }

    private sealed class ThrowingProvider : IScannerMetadataProvider
    {
        public string ProviderName => "Failure";
        public Task<ScannerMetadataProviderResult> GetMetadataAsync(AdapterScannerDevice scanner, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("raw platform failure");
    }

    private sealed class SlowProvider : IScannerMetadataProvider
    {
        public string ProviderName => "Slow";
        public async Task<ScannerMetadataProviderResult> GetMetadataAsync(AdapterScannerDevice scanner, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            return ScannerMetadataProviderResult.Available(null);
        }
    }

    private sealed class StaticProvider : IScannerMetadataProvider
    {
        public string ProviderName => "Static";
        public Task<ScannerMetadataProviderResult> GetMetadataAsync(AdapterScannerDevice scanner, CancellationToken cancellationToken) =>
            Task.FromResult(ScannerMetadataProviderResult.Available(new ScannerMetadata(
                "SERIAL8170", "Test", null, null, "PFU", "3.2.1", null, null, null, null, null, null)));
    }

    private sealed class MetadataProvider(ScannerMetadata metadata) : IScannerMetadataProvider
    {
        public string ProviderName => "Metadata";
        public Task<ScannerMetadataProviderResult> GetMetadataAsync(AdapterScannerDevice scanner, CancellationToken cancellationToken) =>
            Task.FromResult(ScannerMetadataProviderResult.Available(metadata));
    }
}
