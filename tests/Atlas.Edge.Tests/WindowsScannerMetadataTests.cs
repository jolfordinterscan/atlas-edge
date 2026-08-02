using Atlas.Edge.ScannerDiscovery;
using Atlas.Edge.Core;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace Atlas.Edge.Tests;

public sealed class WindowsScannerMetadataTests
{
    private static readonly WindowsScannerMetadataRecord FujitsuPnp = new(
        @"USB\VID_04C5&PID_15FF\6&3A91DD4C&0&2",
        [@"USB\VID_04C5&PID_15FF&REV_0100", @"USB\VID_04C5&PID_15FF"],
        "{692D195E-8DF9-11F1-B692-502E910C06AF}",
        ["PCIROOT(0)#PCI(0803)#PCI(0000)#USBROOT(0)#USB(2)"],
        "FUJITSU",
        "fi-8170",
        "fi-8170",
        "FUJITSU",
        "2.0.0.9")
    {
        Service = "usbscan"
    };

    [Fact]
    public async Task PnpProvider_EnrichesSerialDriverUsbAndHashedIdentifiers()
    {
        var provider = new WindowsPnpScannerMetadataProvider(new PnpCatalog(true, [FujitsuPnp]));

        var result = await provider.GetMetadataAsync(WiaScanner(), CancellationToken.None);

        Assert.NotNull(result.Metadata);
        Assert.Null(result.Metadata.SerialNumber);
        Assert.Null(result.Metadata.SerialSource);
        Assert.Equal("04C5", result.Metadata.UsbVendorId);
        Assert.Equal("15FF", result.Metadata.UsbProductId);
        Assert.Equal("2.0.0.9", result.Metadata.DriverVersion);
        Assert.Equal("FUJITSU", result.Metadata.DriverProvider);
        Assert.Equal("fi-8170", result.Metadata.FriendlyName);
        Assert.Equal("ExactDeviceInstance", result.Diagnostic!.MatchStrategy);
        Assert.All(
            [result.Metadata.HardwareId, result.Metadata.ContainerId, result.Metadata.LocationPathHash, result.Metadata.DeviceInstanceIdHash],
            value => Assert.Matches("^[a-f0-9]{64}$", value!));
        Assert.DoesNotContain("VID_04C5", string.Join('|', result.Metadata.HardwareId, result.Metadata.DeviceInstanceIdHash));
    }

    [Fact]
    public async Task SerialAbsent_RemainsUnknownWithoutInference()
    {
        var record = FujitsuPnp with { DeviceInstanceId = @"USB\VID_04C5&PID_15FF\6&ABC&0&3" };
        var result = await new WindowsPnpScannerMetadataProvider(new PnpCatalog(true, [record]))
            .GetMetadataAsync(WiaScanner(), CancellationToken.None);

        Assert.Null(result.Metadata!.SerialNumber);
        Assert.Null(result.Metadata.SerialSource);
    }

    [Theory]
    [InlineData(@"USB\VID_04C5&PID_15FF\6&3A91DD4C&0&2")]
    [InlineData(@"USB\VID_04C5&PID_15FF\7&ABCDEF&0&1")]
    [InlineData(@"USB\VID_04C5&PID_15FF\PORT&2")]
    public void TopologyStyleInstanceSuffix_IsNeverSerialEvidence(string instanceId)
    {
        var metadata = WindowsPnpScannerMetadataProvider.Normalize(FujitsuPnp with { DeviceInstanceId = instanceId });

        Assert.Null(metadata.SerialNumber);
        Assert.Null(metadata.SerialSource);
    }

    [Fact]
    public void StableLookingInstanceSuffix_IsAcceptedAsPnPSerialEvidence()
    {
        var metadata = WindowsPnpScannerMetadataProvider.Normalize(
            FujitsuPnp with { DeviceInstanceId = @"USB\VID_04C5&PID_15FF\SERIAL8170" });

        Assert.Equal("SERIAL8170", metadata.SerialNumber);
        Assert.Equal("WindowsPnPDeviceInstance", metadata.SerialSource);
    }

    [Fact]
    public async Task RealFujitsuRecord_MatchesUniqueManufacturerAndFriendlyName()
    {
        var result = await Provider(FujitsuPnp).GetMetadataAsync(RealWiaScanner(), CancellationToken.None);

        Assert.NotNull(result.Metadata);
        Assert.Equal("ManufacturerModelUnique", result.Diagnostic!.MatchStrategy);
        Assert.Equal(200, result.Diagnostic.MatchScore);
        Assert.False(result.Diagnostic.IsAmbiguous);
        Assert.Equal("fi-8170", result.Metadata.FriendlyName);
        Assert.Equal("04C5", result.Metadata.UsbVendorId);
        Assert.Equal("15FF", result.Metadata.UsbProductId);
    }

    [Fact]
    public async Task ManufacturerPrefixedModel_MatchesFriendlyName()
    {
        var scanner = RealWiaScanner() with { Model = "FUJITSU fi-8170" };
        var result = await Provider(FujitsuPnp).GetMetadataAsync(scanner, CancellationToken.None);

        Assert.NotNull(result.Metadata);
        Assert.Equal("ManufacturerModelUnique", result.Diagnostic!.MatchStrategy);
    }

    [Fact]
    public async Task DriverNumberSuffix_IsIgnoredOnlyForComparison()
    {
        var record = FujitsuPnp with { FriendlyName = null, DriverName = "fi-8170" };
        var scanner = RealWiaScanner() with { Model = "fi-8170 #3" };
        var result = await Provider(record).GetMetadataAsync(scanner, CancellationToken.None);

        Assert.NotNull(result.Metadata);
        Assert.Equal("fi-8170", result.Metadata.DriverName);
        Assert.Equal("fi-8170 #3", scanner.Model);
    }

    [Fact]
    public async Task ExactVidPidMatch_PrecedesNameFallback()
    {
        var scanner = RealWiaScanner() with
        {
            SourceId = @"wia:USB\VID_04C5&PID_15FF\opaque",
            DevicePath = null,
            Model = "Different model"
        };
        var result = await Provider(FujitsuPnp).GetMetadataAsync(scanner, CancellationToken.None);

        Assert.NotNull(result.Metadata);
        Assert.Equal("ExactVidPid", result.Diagnostic!.MatchStrategy);
    }

    [Fact]
    public async Task UniqueManufacturerScannerClassFallback_RequiresUsbscanService()
    {
        var scanner = RealWiaScanner() with { Model = "Unknown WIA source" };
        var result = await Provider(FujitsuPnp).GetMetadataAsync(scanner, CancellationToken.None);

        Assert.NotNull(result.Metadata);
        Assert.Equal("ManufacturerScannerClassUnique", result.Diagnostic!.MatchStrategy);
    }

    [Fact]
    public async Task ManufacturerOnlyMatch_IsRejectedWithoutScannerClassEvidence()
    {
        var record = FujitsuPnp with { FriendlyName = "Different", DriverName = "Different", Service = "vendor" };
        var result = await Provider(record).GetMetadataAsync(RealWiaScanner(), CancellationToken.None);

        Assert.Null(result.Metadata);
        Assert.Equal("None", result.Diagnostic!.MatchStrategy);
    }

    [Fact]
    public async Task ModelOnlyMatch_IsRejected()
    {
        var record = FujitsuPnp with { Manufacturer = "Other", FriendlyName = "fi-8170", Service = null };
        var result = await Provider(record).GetMetadataAsync(RealWiaScanner(), CancellationToken.None);

        Assert.Null(result.Metadata);
    }

    [Fact]
    public async Task EqualManufacturerModelCandidates_AreAmbiguousAndRejected()
    {
        var second = FujitsuPnp with { DeviceInstanceId = @"USB\VID_04C5&PID_15FF\OTHERDEVICE" };
        var result = await new WindowsPnpScannerMetadataProvider(new PnpCatalog(true, [FujitsuPnp, second]))
            .GetMetadataAsync(RealWiaScanner(), CancellationToken.None);

        Assert.Null(result.Metadata);
        Assert.True(result.Diagnostic!.IsAmbiguous);
        Assert.Equal("ManufacturerModelUnique", result.Diagnostic.MatchStrategy);
    }

    [Fact]
    public async Task PnpDriverVersion_TakesPrecedenceOverWeakerWiaVersion()
    {
        var enricher = new ScannerMetadataEnricher(
            [Provider(FujitsuPnp)], TimeProvider.System, TimeSpan.FromSeconds(1));
        var scanner = RealWiaScanner() with
        {
            Driver = new ScannerDriver("fi-8170 #3", "2.0.0.4", "FUJITSU")
        };

        var enriched = Assert.Single(await enricher.EnrichAsync([scanner], CancellationToken.None));

        Assert.Equal("2.0.0.9", enriched.Driver.Version);
        Assert.Equal("fi-8170", enriched.Driver.Name);
    }

    [Fact]
    public void NameNormalizer_IsCaseInsensitiveTrimmedAndConservative()
    {
        Assert.Equal("FI-8170", ScannerMetadataNameNormalizer.Normalize("  fi-8170 #3 "));
        Assert.Equal("FUJITSU", ScannerMetadataNameNormalizer.Normalize("@oem42.inf,%manufacturer%;FUJITSU"));
        Assert.True(ScannerMetadataNameNormalizer.ModelMatches("FUJITSU fi-8170", "fi-8170", "FUJITSU", "FUJITSU"));
        Assert.False(ScannerMetadataNameNormalizer.ModelMatches("fi-8170", "fi-8170 PRO", "FUJITSU", "FUJITSU"));
    }

    [Fact]
    public async Task MetadataDiagnostic_ContainsOnlySafeMatchFacts()
    {
        var result = await Provider(FujitsuPnp).GetMetadataAsync(RealWiaScanner(), CancellationToken.None);
        var json = JsonSerializer.Serialize(result.Diagnostic);

        Assert.Contains("ManufacturerModelUnique", json, StringComparison.Ordinal);
        Assert.Contains("HardwareIdHash", json, StringComparison.Ordinal);
        Assert.DoesNotContain(FujitsuPnp.DeviceInstanceId, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(FujitsuPnp.HardwareIds[0], json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(FujitsuPnp.ContainerId!, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(FujitsuPnp.LocationPaths[0], json, StringComparison.OrdinalIgnoreCase);
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
        Assert.Null(scanner.SerialNumber);
        Assert.Equal("2.0.0.9", scanner.Drivers[0].Version);
        Assert.Null(scanner.FirmwareVersion);
        Assert.Equal("04C5", scanner.UsbVendorId);
        var inventory = new ScannerInventoryEventBuilder().Build(
            snapshot,
            new AgentIdentity("agent", "workstation", "tenant", "Test", false, DateTimeOffset.UtcNow));
        var entry = Assert.Single(inventory.Scanners);
        Assert.Equal(metadata.DeviceInstanceIdHash, entry.DeviceInstanceIdHash);
        var json = JsonSerializer.Serialize(inventory);
        Assert.DoesNotContain(FujitsuPnp.DeviceInstanceId, json, StringComparison.OrdinalIgnoreCase);
        Assert.All(FujitsuPnp.HardwareIds, value =>
            Assert.DoesNotContain(value, json, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(FujitsuPnp.LocationPaths[0], json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(StillImageRegistryPathFragment, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Enrichment_DoesNotChangeProviderStableScannerIdentity()
    {
        var scanner = RealWiaScanner();
        var identityFactory = new ScannerIdentityFactory();
        var before = identityFactory.Create(scanner).ScannerId;
        var enriched = Assert.Single(await new ScannerMetadataEnricher(
            [Provider(FujitsuPnp)], TimeProvider.System, TimeSpan.FromSeconds(1))
            .EnrichAsync([scanner], CancellationToken.None));

        Assert.Equal(before, identityFactory.Create(enriched).ScannerId);
    }

    [Fact]
    public void WindowsMetadataSource_IsReadOnlyAndHasNoAcquisitionOrCommandSurface()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var source = File.ReadAllText(Path.Combine(root, "src/Atlas.Edge.ScannerDiscovery/WindowsScannerMetadata.cs"));

        Assert.Contains("writable: false", source, StringComparison.Ordinal);
        Assert.Contains("CM_Get_DevNode_PropertyW", source, StringComparison.Ordinal);
        Assert.Contains("HardwareIds", source, StringComparison.Ordinal);
        Assert.Contains("LocationPaths", source, StringComparison.Ordinal);
        Assert.Contains("DriverVersion", source, StringComparison.Ordinal);
        Assert.Contains("Service", source, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "SetValue(", "CreateSubKey", "DeleteSubKey", "Transfer(", "ShowAcquireImage", "ShowSelectDevice", "scanner_command", "remote_control" })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static WindowsPnpScannerMetadataProvider Provider(WindowsScannerMetadataRecord record) =>
        new(new PnpCatalog(true, [record]));

    private static AdapterScannerDevice WiaScanner() => new(
        @"USB\VID_04C5&PID_15FF\6&3A91DD4C&0&2",
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
        DevicePath = @"USB\VID_04C5&PID_15FF\6&3A91DD4C&0&2"
    };

    private static AdapterScannerDevice RealWiaScanner() => WiaScanner() with
    {
        SourceId = "wia-fi-8170-stable-source",
        DevicePath = null,
        Driver = new ScannerDriver("fi-8170 #3", "2.0.0.4", "FUJITSU")
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
