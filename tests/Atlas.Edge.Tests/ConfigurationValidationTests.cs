using Atlas.Edge.Configuration;
using Microsoft.Extensions.Options;

namespace Atlas.Edge.Tests;

public sealed class ConfigurationValidationTests
{
    [Fact]
    public void Validate_Fails_WhenRequiredValuesAreMissing()
    {
        var validator = new AtlasEdgeOptionsValidator();
        var options = new AtlasEdgeOptions
        {
            AgentId = string.Empty,
            WorkstationId = string.Empty,
            TenantBinding = string.Empty,
            IngestionUrl = "not-a-url",
            HeartbeatIntervalSeconds = 0,
            QueueBatchSize = 0,
            EnvironmentName = string.Empty
        };

        var result = validator.Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("AgentId", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains("IngestionUrl", StringComparison.Ordinal));
    }
}