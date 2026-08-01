using Microsoft.Extensions.Options;

namespace Atlas.Edge.Configuration;

public sealed class AtlasEdgeOptionsValidator : IValidateOptions<AtlasEdgeOptions>
{
    public ValidateOptionsResult Validate(string? name, AtlasEdgeOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.AgentId))
        {
            errors.Add("AgentId is required.");
        }

        if (string.IsNullOrWhiteSpace(options.WorkstationId))
        {
            errors.Add("WorkstationId is required.");
        }

        if (string.IsNullOrWhiteSpace(options.TenantBinding))
        {
            errors.Add("TenantBinding is required.");
        }

        if (string.IsNullOrWhiteSpace(options.IngestionUrl))
        {
            errors.Add("IngestionUrl is required.");
        }
        else if (!Uri.TryCreate(options.IngestionUrl, UriKind.Absolute, out _))
        {
            errors.Add("IngestionUrl must be an absolute URI.");
        }

        if (options.HeartbeatIntervalSeconds <= 0)
        {
            errors.Add("HeartbeatIntervalSeconds must be greater than zero.");
        }

        if (options.QueueBatchSize <= 0)
        {
            errors.Add("QueueBatchSize must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(options.EnvironmentName))
        {
            errors.Add("EnvironmentName is required.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}