using System.Text.Json.Serialization;

namespace Atlas.Edge.Enrollment;

public sealed record TokenRefreshRequest(
    [property: JsonPropertyName("agent_id")]
    string AgentId,
    [property: JsonPropertyName("device_id")]
    string DeviceId,
    [property: JsonPropertyName("tenant_binding")]
    string TenantBinding,
    [property: JsonPropertyName("refresh_token")]
    string RefreshToken,
    [property: JsonPropertyName("requested_at_utc")]
    DateTimeOffset RequestedAtUtc);

public sealed record TokenRefreshResponse(
    [property: JsonPropertyName("agent_id")]
    string AgentId,
    [property: JsonPropertyName("device_id")]
    string DeviceId,
    [property: JsonPropertyName("tenant_binding")]
    string TenantBinding,
    [property: JsonPropertyName("token_type")]
    string TokenType,
    [property: JsonPropertyName("access_token")]
    string AccessToken,
    [property: JsonPropertyName("refresh_token")]
    string RefreshToken,
    [property: JsonPropertyName("credential_expiry_utc")]
    DateTimeOffset AccessTokenExpiryUtc,
    [property: JsonPropertyName("refresh_token_expiry_utc")]
    DateTimeOffset RefreshTokenExpiryUtc);

public enum TokenRefreshFailureKind
{
    None = 0,
    Transient = 1,
    InvalidRefreshToken = 2,
    RevokedRefreshToken = 3,
    ExpiredRefreshToken = 4,
    BindingMismatch = 5,
    InvalidResponse = 6,
    EndpointRejected = 7
}

public sealed record TokenRefreshResult(
    TokenRefreshResponse? Response,
    TokenRefreshFailureKind FailureKind,
    int? StatusCode,
    string? ErrorCode)
{
    public bool IsSuccess => Response is not null && FailureKind == TokenRefreshFailureKind.None;

    public bool IsPermanent => FailureKind is
        TokenRefreshFailureKind.InvalidRefreshToken or
        TokenRefreshFailureKind.RevokedRefreshToken or
        TokenRefreshFailureKind.ExpiredRefreshToken or
        TokenRefreshFailureKind.BindingMismatch or
        TokenRefreshFailureKind.EndpointRejected;

    public static TokenRefreshResult Success(TokenRefreshResponse response) =>
        new(response, TokenRefreshFailureKind.None, 200, null);

    public static TokenRefreshResult Failure(TokenRefreshFailureKind kind, int? statusCode, string? errorCode) =>
        new(null, kind, statusCode, errorCode);
}
