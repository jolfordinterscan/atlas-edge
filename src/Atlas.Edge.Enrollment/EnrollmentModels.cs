using System.Text.Json.Serialization;

namespace Atlas.Edge.Enrollment;

public sealed record EnrollmentRequest(
    [property: JsonPropertyName("enrollment_code")]
    string EnrollmentCode,
    [property: JsonPropertyName("environment_name")]
    string EnvironmentName,
    [property: JsonPropertyName("machine_name")]
    string MachineName,
    [property: JsonPropertyName("requested_at_utc")]
    DateTimeOffset RequestedAtUtc);

public sealed record EnrollmentResponse(
    [property: JsonPropertyName("agent_id")]
    string AgentId,
    [property: JsonPropertyName("device_id")]
    string DeviceId,
    [property: JsonPropertyName("tenant_binding")]
    string TenantBinding,
    [property: JsonPropertyName("ingestion_url")]
    string IngestionUrl,
    [property: JsonPropertyName("site_timezone")]
    string SiteTimezone,
    [property: JsonPropertyName("access_token")]
    string AccessToken,
    [property: JsonPropertyName("refresh_token")]
    string RefreshToken,
    [property: JsonPropertyName("credential_expiry_utc")]
    DateTimeOffset CredentialExpiryUtc,
    [property: JsonPropertyName("refresh_token_expiry_utc")]
    DateTimeOffset RefreshTokenExpiryUtc,
    [property: JsonPropertyName("token_refresh_url")]
    string TokenRefreshUrl);

public sealed record EnrollmentResult(
    EnrollmentResponse? Response,
    bool IsRetryable,
    string? Error)
{
    public static EnrollmentResult Success(EnrollmentResponse response) =>
        new(response, false, null);

    public static EnrollmentResult RetryableFailure(string error) =>
        new(null, true, error);

    public static EnrollmentResult NonRetryableFailure(string error) =>
        new(null, false, error);
}
