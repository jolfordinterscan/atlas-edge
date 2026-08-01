using Microsoft.Extensions.Options;
using Atlas.Edge.Core;

namespace Atlas.Edge.Configuration;

public sealed class AtlasEdgeOptionsValidator : IValidateOptions<AtlasEdgeOptions>
{
    public ValidateOptionsResult Validate(string? name, AtlasEdgeOptions options)
    {
        var errors = new List<string>();
        var isHttpTransport = string.Equals(
            options.TransportMode,
            AtlasEdgeOptions.TransportModeHttp,
            StringComparison.OrdinalIgnoreCase);
        var allowInsecureHttp = EndpointSecurityPolicy.IsDevelopmentOverrideEnabled(
            options.EnvironmentName,
            options.AllowInsecureHttpForDevelopment);

        if (!isHttpTransport && string.IsNullOrWhiteSpace(options.AgentId))
        {
            errors.Add("AgentId is required for null transport fallback.");
        }

        if (!isHttpTransport && string.IsNullOrWhiteSpace(options.WorkstationId))
        {
            errors.Add("WorkstationId is required for null transport fallback.");
        }

        if (!isHttpTransport && string.IsNullOrWhiteSpace(options.TenantBinding))
        {
            errors.Add("TenantBinding is required for null transport fallback.");
        }

        if (!isHttpTransport)
        {
            if (string.IsNullOrWhiteSpace(options.IngestionUrl))
            {
                errors.Add("IngestionUrl is required for null transport fallback.");
            }
            else if (!Uri.TryCreate(options.IngestionUrl, UriKind.Absolute, out var ingestionUri))
            {
                errors.Add("IngestionUrl must be an absolute URI.");
            }
            else if (!new EndpointSecurityPolicy(allowInsecureHttp).IsAllowed(ingestionUri))
            {
                errors.Add("IngestionUrl must use HTTPS unless the Development HTTP override is enabled.");
            }
        }

        if (options.HeartbeatIntervalSeconds <= 0)
        {
            errors.Add("HeartbeatIntervalSeconds must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(options.EnrollmentUrl))
        {
            errors.Add("EnrollmentUrl is required.");
        }
        else if (!Uri.TryCreate(options.EnrollmentUrl, UriKind.Absolute, out var enrollmentUri))
        {
            errors.Add("EnrollmentUrl must be an absolute URI.");
        }
        else if (!new EndpointSecurityPolicy(allowInsecureHttp).IsAllowed(enrollmentUri))
        {
            errors.Add("EnrollmentUrl must use HTTPS unless the Development HTTP override is enabled.");
        }

        if (options.AllowInsecureHttpForDevelopment && !allowInsecureHttp)
        {
            errors.Add("AllowInsecureHttpForDevelopment can only be enabled when EnvironmentName is Development.");
        }

        if (options.HttpTimeoutSeconds <= 0)
        {
            errors.Add("HttpTimeoutSeconds must be greater than zero.");
        }

        if (!string.Equals(options.TransportMode, AtlasEdgeOptions.TransportModeNull, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(options.TransportMode, AtlasEdgeOptions.TransportModeHttp, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("TransportMode must be either Null or Http.");
        }

        if (isHttpTransport &&
            string.IsNullOrWhiteSpace(options.CredentialStorePath) &&
            OperatingSystem.IsWindows())
        {
            errors.Add("CredentialStorePath is required for development-mode HTTP transport on Windows until protected store is implemented.");
        }

        if (string.IsNullOrWhiteSpace(options.SiteTimezone))
        {
            errors.Add("SiteTimezone is required.");
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
