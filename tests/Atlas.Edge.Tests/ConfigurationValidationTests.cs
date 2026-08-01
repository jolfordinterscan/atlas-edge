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
            EnrollmentUrl = "not-a-url",
            EnrollmentCode = string.Empty,
            HttpTimeoutSeconds = 0,
            TransportMode = "Invalid",
            SiteTimezone = string.Empty,
            HeartbeatIntervalSeconds = 0,
            QueueBatchSize = 0,
            EnvironmentName = string.Empty
        };

        var result = validator.Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("AgentId", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains("IngestionUrl", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains("HeartbeatIntervalSeconds", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains("SiteTimezone", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains("EnrollmentUrl", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains("TransportMode", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsHttpEndpoints_WithoutDevelopmentOverride()
    {
        var options = new AtlasEdgeOptions
        {
            EnrollmentUrl = "http://localhost:5143/",
            IngestionUrl = "http://localhost:5143/",
            EnvironmentName = "Development",
            AllowInsecureHttpForDevelopment = false
        };

        var result = new AtlasEdgeOptionsValidator().Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("EnrollmentUrl", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains("IngestionUrl", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AllowsHttpEndpoints_WithExplicitDevelopmentOverride()
    {
        var options = new AtlasEdgeOptions
        {
            EnrollmentUrl = "http://localhost:5143/",
            IngestionUrl = "http://localhost:5143/",
            EnvironmentName = "Development",
            AllowInsecureHttpForDevelopment = true
        };

        var result = new AtlasEdgeOptionsValidator().Validate(Options.DefaultName, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_RejectsDevelopmentOverride_OutsideDevelopment()
    {
        var options = new AtlasEdgeOptions
        {
            EnvironmentName = "Production",
            AllowInsecureHttpForDevelopment = true
        };

        var result = new AtlasEdgeOptionsValidator().Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("AllowInsecureHttpForDevelopment", StringComparison.Ordinal));
    }
}
