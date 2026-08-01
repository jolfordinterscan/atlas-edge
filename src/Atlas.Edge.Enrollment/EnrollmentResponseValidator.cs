using Atlas.Edge.Core;

namespace Atlas.Edge.Enrollment;

public static class EnrollmentResponseValidator
{
    public static string? Validate(EnrollmentResponse response, EndpointSecurityPolicy endpointSecurityPolicy)
    {
        if (string.IsNullOrWhiteSpace(response.AgentId))
        {
            return "Enrollment response missing agent_id.";
        }

        if (string.IsNullOrWhiteSpace(response.DeviceId))
        {
            return "Enrollment response missing device_id.";
        }

        if (string.IsNullOrWhiteSpace(response.TenantBinding))
        {
            return "Enrollment response missing tenant_binding.";
        }

        if (string.IsNullOrWhiteSpace(response.SiteTimezone))
        {
            return "Enrollment response missing site_timezone.";
        }

        if (string.IsNullOrWhiteSpace(response.AccessToken))
        {
            return "Enrollment response missing access_token.";
        }

        if (string.IsNullOrWhiteSpace(response.RefreshToken))
        {
            return "Enrollment response missing refresh_token placeholder.";
        }

        if (!Uri.TryCreate(response.IngestionUrl, UriKind.Absolute, out var ingestionUri))
        {
            return "Enrollment response ingestion_url must be an absolute URI.";
        }

        if (!endpointSecurityPolicy.IsAllowed(ingestionUri))
        {
            return "Enrollment response ingestion_url must use HTTPS.";
        }

        if (response.CredentialExpiryUtc == default)
        {
            return "Enrollment response missing credential_expiry_utc.";
        }

        return null;
    }
}
