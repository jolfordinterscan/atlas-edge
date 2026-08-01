using Atlas.Edge.Configuration;
using Microsoft.Extensions.Options;

namespace Atlas.Edge.Tests;

public sealed class ScannerEvidenceConfigurationTests
{
    [Fact]
    public void Defaults_AreConservativeAndValid()
    {
        var options = new AtlasEdgeOptions();

        var result = Validate(options);

        Assert.True(result.Succeeded);
        Assert.False(options.ScannerEvidenceEnabled);
        Assert.Empty(options.ScannerEvidenceProviders);
        Assert.Empty(options.ScannerEvidenceRegistryPaths);
        Assert.Empty(options.ScannerEvidenceLogDirectories);
        Assert.Empty(options.ScannerEvidenceNetworkTargets);
        Assert.False(options.ScannerEvidenceSnmpEnabled);
        Assert.False(options.ScannerEvidenceAllowTlsBypass);
    }

    [Fact]
    public void Validate_RejectsUnknownProviderAndProductionMock()
    {
        var unknown = new AtlasEdgeOptions { ScannerEvidenceProviders = ["VendorSdk"] };
        var productionMock = new AtlasEdgeOptions
        {
            ScannerEvidenceEnabled = true,
            ScannerEvidenceMode = AtlasEdgeOptions.ScannerEvidenceModeMock,
            ScannerEvidenceProviders = ["Mock"],
            EnvironmentName = "Production"
        };

        Assert.Contains(Validate(unknown).Failures!, failure => failure.Contains("unknown provider", StringComparison.Ordinal));
        Assert.Contains(Validate(productionMock).Failures!, failure => failure.Contains("Development", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsUnsafeFilesystemAndRegistryAllowlists()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var options = new AtlasEdgeOptions
        {
            ScannerEvidenceLogDirectories = [root, "relative", Path.Combine(Path.GetTempPath(), "*")],
            ScannerEvidenceLogFiles = [Path.Combine(Path.GetTempPath(), "outside.log")],
            ScannerEvidenceRegistryPaths = [@"HKLM\SOFTWARE", @"HKLM\SOFTWARE\*"]
        };

        var result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("LogDirectories", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains("LogFiles", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains("RegistryPaths", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RestrictsNetworkTargetsAndProhibitsTlsBypass()
    {
        var productionHttp = new AtlasEdgeOptions
        {
            ScannerEvidenceNetworkTargets = ["http://scanner.example/status"],
            ScannerEvidenceAllowTlsBypass = true
        };
        var developmentMock = new AtlasEdgeOptions
        {
            ScannerEvidenceMode = AtlasEdgeOptions.ScannerEvidenceModeMock,
            ScannerEvidenceNetworkTargets = ["http://localhost/status"],
            EnvironmentName = "Development"
        };

        var rejected = Validate(productionHttp);
        Assert.Contains(rejected.Failures!, failure => failure.Contains("HTTPS", StringComparison.Ordinal));
        Assert.Contains(rejected.Failures!, failure => failure.Contains("prohibited", StringComparison.Ordinal));
        Assert.True(Validate(developmentMock).Succeeded);
    }

    [Fact]
    public void Validate_RequiresSnmpFlagAndReadLimitWithinFileLimit()
    {
        var disabled = new AtlasEdgeOptions
        {
            ScannerEvidenceNetworkTargets = ["snmp://scanner.example"],
            ScannerEvidenceMaximumFileSizeBytes = 100,
            ScannerEvidenceMaximumReadBytes = 101
        };
        var enabled = new AtlasEdgeOptions
        {
            ScannerEvidenceNetworkTargets = ["snmp://scanner.example"],
            ScannerEvidenceSnmpEnabled = true
        };

        var rejected = Validate(disabled);
        Assert.Contains(rejected.Failures!, failure => failure.Contains("NetworkTargets", StringComparison.Ordinal));
        Assert.Contains(rejected.Failures!, failure => failure.Contains("MaximumReadBytes", StringComparison.Ordinal));
        Assert.True(Validate(enabled).Succeeded);
    }

    private static ValidateOptionsResult Validate(AtlasEdgeOptions options) =>
        new AtlasEdgeOptionsValidator().Validate(Options.DefaultName, options);
}
