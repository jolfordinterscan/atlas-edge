using System.Text.Json;
using Atlas.Edge.ScannerDiscovery;

namespace Atlas.Edge.Tests;

public sealed class InoTecInvestigationTests
{
    [Theory]
    [InlineData("WIA InoTec SCAMAX USB3")]
    [InlineData("DATAWIN scanner service")]
    [InlineData("InoTec GmbH")]
    public void Classifier_RecognizesInoTecAndScamaxComponents(string value)
    {
        Assert.True(InoTecEvidenceClassifier.IsInoTec(value));
    }

    [Theory]
    [InlineData("Canon MF620C Series")]
    [InlineData("FUJITSU fi-8170")]
    [InlineData("")]
    public void Classifier_RejectsUnrelatedComponents(string value)
    {
        Assert.False(InoTecEvidenceClassifier.IsInoTec(value));
    }

    [Fact]
    public void Classifier_IdentifiesPromisingMetadataFunctionNames()
    {
        var result = InoTecEvidenceClassifier.Classify(
            InoTecInterfaceKind.NativeLibrary,
            "GetSerialNumber GetFirmwareVersion GetLifetimePageCount GetRollerCounter " +
            "GetDeviceHealth GetLastError GetMaintenanceCounter");

        Assert.Equal(Enum.GetValues<InoTecMetadataKind>(), result.Select(value => value.Metadata));
        Assert.All(result, value => Assert.Equal(InoTecOpportunityRating.Promising, value.Rating));
    }

    [Fact]
    public void Classifier_TreatsRegisteredInterfacesAsPossibleWithoutClaimingSupport()
    {
        var result = InoTecEvidenceClassifier.Classify(InoTecInterfaceKind.ComTypeLibrary, "SCAMAX API");

        Assert.Equal(Enum.GetValues<InoTecMetadataKind>(), result.Select(value => value.Metadata));
        Assert.All(result, value =>
        {
            Assert.Equal(InoTecOpportunityRating.Possible, value.Rating);
            Assert.Equal("interface_requires_documentation", value.ReasonCode);
        });
    }

    [Fact]
    public async Task Investigator_ReturnsStructuredDeduplicatedInventoryForConnectedWiaSource()
    {
        var evidence = Evidence(
            InoTecInterfaceKind.WiaSource,
            "WIA InoTec SCAMAX USB3",
            new Dictionary<string, string> { ["Manufacturer"] = "InoTec" });
        var investigator = new InoTecInvestigator(
            [new StaticSource("WIA", true, [evidence, evidence])],
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero)));

        var snapshot = await investigator.InspectAsync(CancellationToken.None);

        Assert.True(snapshot.IsAvailable);
        Assert.Equal("1.0", snapshot.SchemaVersion);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero), snapshot.CollectedAtUtc);
        var component = Assert.Single(snapshot.Interfaces);
        Assert.Equal("WIA InoTec SCAMAX USB3", component.Name);
        Assert.Equal(InoTecInterfaceKind.WiaSource, component.Kind);
        Assert.Empty(snapshot.Diagnostics);
    }

    [Fact]
    public async Task Investigator_IsolatesSourceFailureWithStableDiagnostic()
    {
        var investigator = new InoTecInvestigator(
            [new ThrowingSource(), new StaticSource("PnP", true, [Evidence(InoTecInterfaceKind.WindowsPnp, "SCAMAX")])],
            TimeProvider.System);

        var snapshot = await investigator.InspectAsync(CancellationToken.None);

        Assert.Single(snapshot.Interfaces);
        var diagnostic = Assert.Single(snapshot.Diagnostics);
        Assert.Equal("Broken", diagnostic.Source);
        Assert.Equal("inotec_source_failure", diagnostic.ErrorCode);
        Assert.DoesNotContain("sensitive", JsonSerializer.Serialize(snapshot), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Investigator_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var investigator = new InoTecInvestigator([new CancelingSource()], TimeProvider.System);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => investigator.InspectAsync(cancellation.Token));
    }

    [Fact]
    public async Task WindowsSource_IsUnavailableWithoutFabricatingEvidenceOnNonWindows()
    {
        if (OperatingSystem.IsWindows()) return;

        var result = await new WindowsInoTecInvestigationSource().InspectAsync(CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Empty(result.Interfaces);
    }

    [Fact]
    public void PrivacyHash_IsStableAndDoesNotExposeRawMachineIdentifier()
    {
        const string raw = @"USB\VID_1234&PID_5678\MACHINE-SPECIFIC";

        var first = InoTecInvestigationPrivacy.HashIdentifier(raw);
        var second = InoTecInvestigationPrivacy.HashIdentifier(raw.ToLowerInvariant());

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.DoesNotContain("MACHINE", first, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PortableExecutableInspection_FailsClosedForNonPeData()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "not a portable executable");

            Assert.Empty(PortableExecutableExportReader.ReadExportNames(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Implementation_IsReadOnlyAndNeverLoadsOrExecutesVendorCode()
    {
        var source = ReadSource("src/Atlas.Edge.ScannerDiscovery/InoTecInvestigation.cs");
        var probe = ReadSource("tools/Atlas.Edge.InoTecProbe/Program.cs");

        Assert.Contains("writable: false", source, StringComparison.Ordinal);
        Assert.Contains("StaticOnly", source, StringComparison.Ordinal);
        Assert.Contains("ContentsRead\"] = \"False", source, StringComparison.Ordinal);
        Assert.Contains("ReparsePoint", source, StringComparison.Ordinal);
        foreach (var forbidden in new[]
        {
            "SetValue(", "CreateSubKey", "DeleteSubKey", "RegistryKey.Create", "LoadLibrary",
            "Assembly.Load", "Activator.CreateInstance", "Process.Start", "PowerShell", "cmd.exe",
            "Transfer(", "ShowAcquireImage", "AcquireImage", "OpenScanner", "ResetCounter",
            "scanner command", "firmware update"
        })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(forbidden, probe, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static InoTecInterfaceEvidence Evidence(
        InoTecInterfaceKind kind,
        string name,
        IReadOnlyDictionary<string, string>? properties = null) =>
        new(
            kind,
            name,
            null,
            null,
            VendorSoftwareArchitecture.Unknown,
            properties ?? new Dictionary<string, string>(),
            [],
            InoTecEvidenceClassifier.Classify(kind, name));

    private static string ReadSource(string relativePath)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        return File.ReadAllText(Path.Combine(root, relativePath));
    }

    private sealed class StaticSource(
        string sourceName,
        bool available,
        IReadOnlyList<InoTecInterfaceEvidence> interfaces) : IInoTecInvestigationSource
    {
        public string SourceName => sourceName;

        public Task<InoTecInvestigationSourceResult> InspectAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new InoTecInvestigationSourceResult(available, interfaces, []));
    }

    private sealed class ThrowingSource : IInoTecInvestigationSource
    {
        public string SourceName => "Broken";

        public Task<InoTecInvestigationSourceResult> InspectAsync(
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("sensitive platform detail");
    }

    private sealed class CancelingSource : IInoTecInvestigationSource
    {
        public string SourceName => "Canceled";

        public Task<InoTecInvestigationSourceResult> InspectAsync(
            CancellationToken cancellationToken) =>
            Task.FromCanceled<InoTecInvestigationSourceResult>(cancellationToken);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
