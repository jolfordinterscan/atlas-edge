using Atlas.Edge.ScannerDiscovery;
using Atlas.Edge.ScannerEvidence;

namespace Atlas.Edge.Tests;

public sealed class ScannerEvidenceConnectorAdapterTests
{
    [Fact]
    public async Task WiaEvidenceProvider_ReusesConnectorNormalizationAndPreservesUnknowns()
    {
        var catalog = new StaticCatalog(new ScannerSourceCatalogResult(
            true,
            [new ScannerSourceMetadata(
                "source-1",
                "Acme",
                "ScanPro",
                "SERIAL-1",
                null,
                "USB",
                true,
                null,
                true,
                ["duplex"],
                new ScannerDriver("Driver", "2.0", "Acme"),
                ScannerOnlineStatus.Online)]));
        using var provider = new WiaScannerEvidenceProvider(catalog);

        Assert.Equal(EvidenceValueState.Known, (await provider.CheckAvailabilityAsync(CancellationToken.None)).State);
        var target = Assert.Single((await provider.DiscoverTargetsAsync(CancellationToken.None)).Value);
        var identity = (await provider.ReadIdentityAsync(target, CancellationToken.None)).Value;
        var firmware = (await provider.ReadFirmwareAsync(target, CancellationToken.None)).Value;
        var connection = (await provider.ReadConnectionAsync(target, CancellationToken.None)).Value;

        Assert.Equal(EvidenceSourceQuality.StandardProtocol, provider.Descriptor.SourceQuality);
        Assert.Equal("Acme", identity.Manufacturer.Value);
        Assert.Equal(EvidenceValueState.Unknown, identity.HardwareInstanceId.State);
        Assert.Equal(EvidenceValueState.Unknown, firmware.Version.State);
        Assert.True(connection.Present.Value);
        Assert.DoesNotContain("SERIAL-1", target.TargetId, StringComparison.Ordinal);
    }

    [Fact]
    public void ProtocolEvidenceProviders_DeclareOnlyMappedReadCapabilities()
    {
        var wia = new WiaScannerEvidenceProvider(new StaticCatalog(new ScannerSourceCatalogResult(false, [])));
        var twain = new TwainScannerEvidenceProvider(new StaticCatalog(new ScannerSourceCatalogResult(false, [])));
        var isis = new IsisScannerEvidenceProvider(new StaticCatalog(new ScannerSourceCatalogResult(false, [])));
        using (wia)
        using (twain)
        using (isis)
        {
            Assert.All(new IScannerEvidenceProvider[] { wia, twain, isis }, provider =>
            {
                Assert.Contains(EvidenceCapability.DeviceIdentity, provider.Descriptor.Capabilities);
                Assert.Contains(EvidenceCapability.Counters, provider.Descriptor.Capabilities);
                Assert.DoesNotContain(EvidenceCapability.Services, provider.Descriptor.Capabilities);
                Assert.DoesNotContain(EvidenceCapability.Network, provider.Descriptor.Capabilities);
            });
        }
    }

    private sealed class StaticCatalog :
        IWiaScannerSourceCatalog,
        ITwainScannerSourceCatalog,
        IIsisScannerSourceCatalog
    {
        private readonly ScannerSourceCatalogResult _result;

        public StaticCatalog(ScannerSourceCatalogResult result) => _result = result;

        public Task<ScannerSourceCatalogResult> EnumerateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_result);
    }
}
