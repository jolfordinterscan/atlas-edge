using Atlas.Edge.ScannerDiscovery;

namespace Atlas.Edge.Tests;

public sealed class VendorMetadataProviderTests
{
    private static readonly VendorInstallation PaperStreamInstallation = new(
        "PaperStream",
        "PaperStream IP (TWAIN)",
        "3.40.1",
        @"C:\Program Files\PFU\PaperStream IP",
        VendorSoftwareArchitecture.X64,
        VendorInstallationSourceKind.InstalledProgram,
        [
            new VendorSdkCandidate(
                "PaperStreamSdkCandidate.dll",
                @"C:\Program Files\PFU\PaperStream IP\PaperStreamSdkCandidate.dll",
                "3.40.1",
                VendorSoftwareArchitecture.X64,
                VendorInterfaceKind.NativeLibrary)
        ]);

    [Fact]
    public async Task Catalog_AggregatesAndDeduplicatesImmutableInstallations()
    {
        var catalog = new VendorInstallationCatalog([
            new StaticSource(true, [PaperStreamInstallation]),
            new StaticSource(true, [PaperStreamInstallation])
        ]);

        var snapshot = await catalog.DiscoverAsync(CancellationToken.None);

        Assert.True(snapshot.IsAvailable);
        var installation = Assert.Single(snapshot.Installations);
        Assert.Equal("PaperStream", installation.Vendor);
        Assert.Equal("3.40.1", installation.Version);
        Assert.Single(installation.SdkCandidates);
    }

    [Fact]
    public async Task Catalog_IsolatesSourceFailureWithStableDiagnostic()
    {
        var catalog = new VendorInstallationCatalog([
            new ThrowingSource(),
            new StaticSource(true, [PaperStreamInstallation])
        ]);

        var snapshot = await catalog.DiscoverAsync(CancellationToken.None);

        Assert.Single(snapshot.Installations);
        Assert.Contains(snapshot.Diagnostics, value => value.ErrorCode == "vendor_source_failure");
        Assert.DoesNotContain(snapshot.Diagnostics, value => value.ErrorCode.Contains("platform exception", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Catalog_PropagatesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var catalog = new VendorInstallationCatalog([new CancelingSource()]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => catalog.DiscoverAsync(cancellation.Token));
    }

    [Fact]
    public void PaperStreamStub_SeparatesInstallationFromMetadataSupport()
    {
        var provider = new PaperStreamMetadataProvider();
        var status = provider.Detect(Snapshot(PaperStreamInstallation));

        Assert.True(status.IsInstalled);
        Assert.Equal(VendorMetadataAvailability.Available, status.Availability);
        Assert.All(status.Capabilities, capability =>
        {
            Assert.Equal(VendorMetadataAvailability.Unsupported, capability.Availability);
            Assert.Equal("vendor_adapter_not_implemented", capability.ReasonCode);
        });
        Assert.Equal(Enum.GetValues<VendorMetadataField>(), status.Capabilities.Select(value => value.Field));
    }

    [Theory]
    [InlineData("PaperStream")]
    [InlineData("Ricoh")]
    [InlineData("PFU")]
    public void StubProviders_ReportUnavailableWhenSoftwareIsNotDetected(string providerName)
    {
        var provider = Assert.Single(
            VendorMetadataProviderFactory.CreateDetectionProviders(),
            value => value.ProviderName == providerName);

        var status = provider.Detect(new VendorInstallationSnapshot(true, [], []));

        Assert.False(status.IsInstalled);
        Assert.Equal(VendorMetadataAvailability.Unavailable, status.Availability);
        Assert.All(status.Capabilities, capability =>
            Assert.Equal(VendorMetadataAvailability.Unavailable, capability.Availability));
    }

    [Fact]
    public void RicohAndPfuStubs_MatchOnlyTheirOwnDetectedVendor()
    {
        var installations = Snapshot(
            PaperStreamInstallation,
            PaperStreamInstallation with { Vendor = "Ricoh", ProductName = "RICOH Scanner Control Runtime" },
            PaperStreamInstallation with { Vendor = "PFU", ProductName = "PFU Software Operation Panel" });

        var results = VendorMetadataProviderFactory.CreateDetectionProviders()
            .Select(provider => provider.Detect(installations))
            .ToArray();

        Assert.All(results, result => Assert.True(result.IsInstalled));
        Assert.Equal(["PaperStream", "PFU", "Ricoh"], results.Select(value => value.ProviderName).Order().ToArray());
    }

    [Fact]
    public void NoOpProvider_ReportsEveryFieldUnsupported()
    {
        var status = VendorMetadataProviderFactory.CreateNoOp().Detect(Snapshot(PaperStreamInstallation));

        Assert.False(status.IsInstalled);
        Assert.Equal(VendorMetadataAvailability.Unsupported, status.Availability);
        Assert.All(status.Capabilities, value =>
            Assert.Equal(VendorMetadataAvailability.Unsupported, value.Availability));
    }

    [Fact]
    public async Task WindowsCatalog_IsUnavailableWithoutFabricatingComponentsOnNonWindows()
    {
        if (OperatingSystem.IsWindows()) return;

        var snapshot = await VendorInstallationCatalog.CreateWindowsDefault()
            .DiscoverAsync(CancellationToken.None);

        Assert.False(snapshot.IsAvailable);
        Assert.Empty(snapshot.Installations);
        Assert.Empty(snapshot.Diagnostics);
    }

    [Fact]
    public void VendorCatalogSource_IsReadOnlyBoundedAndDoesNotExecuteOrLoadCandidates()
    {
        var source = ReadSource();

        Assert.Contains("writable: false", source, StringComparison.Ordinal);
        Assert.Contains("MaximumRecordsPerSource", source, StringComparison.Ordinal);
        Assert.Contains("MaximumSdkCandidatesPerInstallation", source, StringComparison.Ordinal);
        Assert.Contains("ReparsePoint", source, StringComparison.Ordinal);
        foreach (var forbidden in new[]
        {
            "SetValue(", "CreateSubKey", "DeleteSubKey", "Process.Start", "Assembly.Load", "LoadLibrary",
            "PowerShell", "cmd.exe", "RegisterTypeLib", "regsvr32", "Transfer(", "ShowAcquireImage",
            "scanner_command", "remote_control", "firmware_update"
        })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void StubProviders_HaveNoMetadataReadOrScannerCommandSurface()
    {
        var methods = new[]
        {
            typeof(NoOpVendorMetadataProvider), typeof(PaperStreamMetadataProvider),
            typeof(RicohMetadataProvider), typeof(PFUMetadataProvider)
        }.SelectMany(type => type.GetMethods().Where(method => method.DeclaringType == type)).ToArray();

        Assert.DoesNotContain(methods, method =>
            method.Name.Contains("Scan", StringComparison.OrdinalIgnoreCase) ||
            method.Name.Contains("Acquire", StringComparison.OrdinalIgnoreCase) ||
            method.Name.Contains("Command", StringComparison.OrdinalIgnoreCase) ||
            method.Name.Contains("PageCount", StringComparison.OrdinalIgnoreCase));
    }

    private static VendorInstallationSnapshot Snapshot(params VendorInstallation[] installations) =>
        new(true, installations, []);

    private static string ReadSource()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        return File.ReadAllText(Path.Combine(
            root,
            "src/Atlas.Edge.ScannerDiscovery/VendorMetadataProviders.cs"));
    }

    private sealed class StaticSource(
        bool available,
        IReadOnlyList<VendorInstallation> installations) : IVendorInstallationSource
    {
        public Task<VendorInstallationSnapshot> DiscoverAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new VendorInstallationSnapshot(available, installations, []));
    }

    private sealed class ThrowingSource : IVendorInstallationSource
    {
        public Task<VendorInstallationSnapshot> DiscoverAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("raw platform exception");
    }

    private sealed class CancelingSource : IVendorInstallationSource
    {
        public Task<VendorInstallationSnapshot> DiscoverAsync(CancellationToken cancellationToken) =>
            Task.FromCanceled<VendorInstallationSnapshot>(cancellationToken);
    }
}
