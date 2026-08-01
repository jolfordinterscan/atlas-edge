namespace Atlas.Edge.Security;

public sealed record CredentialLease(
    string AgentId,
    string DeviceId,
    string TenantBinding,
    string IngestionUrl,
    string SiteTimezone,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiryUtc,
    DateTimeOffset RefreshTokenExpiryUtc,
    string RefreshUrl,
    long Generation,
    string EnvironmentName,
    DateTimeOffset IssuedAtUtc);
