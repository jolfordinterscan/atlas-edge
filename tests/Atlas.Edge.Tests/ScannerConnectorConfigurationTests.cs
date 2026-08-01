using Atlas.Edge.Configuration;
using Microsoft.Extensions.Options;

namespace Atlas.Edge.Tests;

public sealed class ScannerConnectorConfigurationTests
{
    [Fact]
    public void Validate_RejectsUnknownConnectorProvider()
    {
        var options = new AtlasEdgeOptions { ScannerConnectorProvider = "DynamicPlugin" };

        var result = new AtlasEdgeOptionsValidator().Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("ScannerConnectorProvider", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AllowsDevelopmentMockOnlyInDevelopment()
    {
        var development = new AtlasEdgeOptions
        {
            ScannerConnectorsEnabled = true,
            ScannerConnectorProvider = AtlasEdgeOptions.ScannerConnectorProviderMock,
            EnvironmentName = "Development"
        };
        var production = new AtlasEdgeOptions
        {
            ScannerConnectorsEnabled = true,
            ScannerConnectorProvider = AtlasEdgeOptions.ScannerConnectorProviderMock,
            EnvironmentName = "Production"
        };
        var validator = new AtlasEdgeOptionsValidator();

        Assert.True(validator.Validate(Options.DefaultName, development).Succeeded);
        var result = validator.Validate(Options.DefaultName, production);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("Development", StringComparison.Ordinal));
    }
}
