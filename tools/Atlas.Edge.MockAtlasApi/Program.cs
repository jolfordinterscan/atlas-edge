using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables("ATLAS_MOCK_");
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);

var app = builder.Build();
var timeProvider = app.Services.GetRequiredService<TimeProvider>();

var mockConfig = builder.Configuration.GetSection("MockAtlas");
var expectedCode = mockConfig["DevelopmentEnrollmentCode"] ?? "SET_VIA_ATLAS_MOCK_MockAtlas__DevelopmentEnrollmentCode";
var tenantBinding = mockConfig["TenantBinding"] ?? "tenant-dev-a";
var ingestionUrl = mockConfig["IngestionUrl"] ?? "https://localhost:7143/";
var tokenRefreshUrl = mockConfig["TokenRefreshUrl"] ?? "https://localhost:7143/api/edge/v1/token/refresh";
var siteTimezone = mockConfig["SiteTimezone"] ?? "UTC";
var accessTokenTtlSeconds = int.TryParse(mockConfig["AccessTokenTtlSeconds"], out var parsedAccessTtl) && parsedAccessTtl >= 0
    ? parsedAccessTtl
    : 3600;
var refreshTokenTtlSeconds = int.TryParse(mockConfig["RefreshTokenTtlSeconds"], out var parsedRefreshTtl) && parsedRefreshTtl >= 0
    ? parsedRefreshTtl
    : 86400;
var revokeIssuedRefreshTokens = bool.TryParse(mockConfig["RevokeIssuedRefreshTokens"], out var parsedRevoke) && parsedRevoke;

var usedEnrollmentCodes = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
var accessTokenBindings = new ConcurrentDictionary<string, AccessTokenBinding>(StringComparer.Ordinal);
var refreshTokenBindings = new ConcurrentDictionary<string, RefreshTokenBinding>(StringComparer.Ordinal);
var usedRefreshTokens = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
var seenEventIds = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    now_utc = timeProvider.GetUtcNow()
}));

app.MapPost("/api/edge/v1/enroll", (MockEnrollmentRequest request, ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("MockEnrollment");

    if (request is null)
    {
        return Results.BadRequest(new ErrorResponse("invalid_request", "Request body is required.", retryable: false));
    }

    if (string.IsNullOrWhiteSpace(request.enrollment_code))
    {
        return Results.BadRequest(new ErrorResponse("invalid_enrollment_code", "Enrollment code is required.", retryable: false));
    }

    if (!string.Equals(request.enrollment_code, expectedCode, StringComparison.Ordinal))
    {
        logger.LogWarning("Enrollment rejected due to invalid code from machine {MachineName}.", request.machine_name);
        return Results.BadRequest(new ErrorResponse("invalid_enrollment_code", "Enrollment code is invalid.", retryable: false));
    }

    if (!usedEnrollmentCodes.TryAdd(request.enrollment_code, 0))
    {
        logger.LogWarning("Enrollment rejected because code was already used by machine {MachineName}.", request.machine_name);
        return Results.Conflict(new ErrorResponse("enrollment_code_reused", "Enrollment code has already been used.", retryable: false));
    }

    var agentId = $"agent-{Guid.NewGuid():N}";
    var deviceId = $"device-{Guid.NewGuid():N}";
    var accessToken = CreateToken();
    var refreshToken = CreateToken();
    var now = timeProvider.GetUtcNow();
    var expiryUtc = now.AddSeconds(accessTokenTtlSeconds);
    var refreshExpiryUtc = now.AddSeconds(refreshTokenTtlSeconds);

    accessTokenBindings[accessToken] = new AccessTokenBinding(agentId, deviceId, tenantBinding, expiryUtc);
    refreshTokenBindings[refreshToken] = new RefreshTokenBinding(
        agentId,
        deviceId,
        tenantBinding,
        refreshExpiryUtc,
        revokeIssuedRefreshTokens);

    logger.LogInformation("Enrollment accepted for agent {AgentId} and machine {MachineName}.", agentId, request.machine_name);

    return Results.Ok(new MockEnrollmentResponse(
        agent_id: agentId,
        device_id: deviceId,
        tenant_binding: tenantBinding,
        ingestion_url: ingestionUrl,
        site_timezone: siteTimezone,
        access_token: accessToken,
        refresh_token: refreshToken,
        credential_expiry_utc: expiryUtc,
        refresh_token_expiry_utc: refreshExpiryUtc,
        token_refresh_url: tokenRefreshUrl));
});

app.MapPost("/api/edge/v1/token/refresh", (MockTokenRefreshRequest request) =>
{
    if (request is null || string.IsNullOrWhiteSpace(request.refresh_token))
    {
        return Results.Json(
            new ErrorResponse("invalid_refresh_token", "Refresh token is invalid.", retryable: false),
            statusCode: StatusCodes.Status401Unauthorized);
    }

    if (usedRefreshTokens.ContainsKey(request.refresh_token))
    {
        return Results.Json(
            new ErrorResponse("refresh_token_reused", "Refresh token has already been used.", retryable: false),
            statusCode: StatusCodes.Status401Unauthorized);
    }

    if (!refreshTokenBindings.TryGetValue(request.refresh_token, out var binding))
    {
        return Results.Json(
            new ErrorResponse("invalid_refresh_token", "Refresh token is invalid.", retryable: false),
            statusCode: StatusCodes.Status401Unauthorized);
    }

    if (!string.Equals(request.agent_id, binding.AgentId, StringComparison.Ordinal) ||
        !string.Equals(request.device_id, binding.DeviceId, StringComparison.Ordinal) ||
        !string.Equals(request.tenant_binding, binding.TenantBinding, StringComparison.Ordinal))
    {
        return Results.Json(
            new ErrorResponse("binding_mismatch", "Credential binding does not match.", retryable: false),
            statusCode: StatusCodes.Status409Conflict);
    }

    if (binding.Revoked)
    {
        return Results.Json(
            new ErrorResponse("refresh_token_revoked", "Refresh token is revoked.", retryable: false),
            statusCode: StatusCodes.Status403Forbidden);
    }

    if (binding.ExpiryUtc <= timeProvider.GetUtcNow())
    {
        return Results.Json(
            new ErrorResponse("refresh_token_expired", "Refresh token is expired.", retryable: false),
            statusCode: StatusCodes.Status410Gone);
    }

    if (!refreshTokenBindings.TryRemove(request.refresh_token, out _))
    {
        return Results.Json(
            new ErrorResponse("refresh_token_reused", "Refresh token has already been used.", retryable: false),
            statusCode: StatusCodes.Status401Unauthorized);
    }

    usedRefreshTokens[request.refresh_token] = 0;

    var accessToken = CreateToken();
    var refreshToken = CreateToken();
    var now = timeProvider.GetUtcNow();
    var accessExpiryUtc = now.AddSeconds(accessTokenTtlSeconds);
    var refreshExpiryUtc = now.AddSeconds(refreshTokenTtlSeconds);

    accessTokenBindings[accessToken] = new AccessTokenBinding(
        binding.AgentId,
        binding.DeviceId,
        binding.TenantBinding,
        accessExpiryUtc);
    refreshTokenBindings[refreshToken] = new RefreshTokenBinding(
        binding.AgentId,
        binding.DeviceId,
        binding.TenantBinding,
        refreshExpiryUtc,
        revokeIssuedRefreshTokens);

    return Results.Ok(new MockTokenRefreshResponse(
        binding.AgentId,
        binding.DeviceId,
        binding.TenantBinding,
        "Bearer",
        accessToken,
        refreshToken,
        accessExpiryUtc,
        refreshExpiryUtc));
});

app.MapPost("/api/edge/v1/events/batch", (BatchRequest request, HttpContext httpContext) =>
{
    if (!httpContext.Request.Headers.TryGetValue("Authorization", out var authHeader))
    {
        return Results.Json(
            new ErrorResponse("unauthorized", "Authorization header is required.", retryable: false),
            statusCode: StatusCodes.Status401Unauthorized);
    }

    var headerValue = authHeader.ToString();
    if (!headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Json(
            new ErrorResponse("unauthorized", "Bearer token is required.", retryable: false),
            statusCode: StatusCodes.Status401Unauthorized);
    }

    var token = headerValue["Bearer ".Length..].Trim();
    if (string.IsNullOrWhiteSpace(token) || !accessTokenBindings.TryGetValue(token, out var binding))
    {
        return Results.Json(
            new ErrorResponse("unauthorized", "Bearer token is invalid.", retryable: false),
            statusCode: StatusCodes.Status401Unauthorized);
    }

    if (binding.ExpiryUtc <= timeProvider.GetUtcNow())
    {
        return Results.Json(
            new ErrorResponse("access_token_expired", "Access token is expired.", retryable: true),
            statusCode: StatusCodes.Status401Unauthorized);
    }

    if (!string.Equals(request.agentId, binding.AgentId, StringComparison.Ordinal) ||
        !string.Equals(request.tenantBinding, binding.TenantBinding, StringComparison.Ordinal))
    {
        return Results.Json(
            new ErrorResponse("binding_mismatch", "Agent or tenant binding does not match token.", retryable: false),
            statusCode: StatusCodes.Status403Forbidden);
    }

    var accepted = new List<string>();
    var duplicates = new List<string>();

    foreach (var evt in request.events)
    {
        if (!string.Equals(evt.eventType, "agent.heartbeat", StringComparison.Ordinal))
        {
            return Results.Json(
                new ErrorResponse("unsupported_event_type", "Only agent.heartbeat is currently supported.", retryable: false),
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!seenEventIds.TryAdd(evt.eventId, 0))
        {
            duplicates.Add(evt.eventId);
            continue;
        }

        accepted.Add(evt.eventId);
    }

    if (duplicates.Count > 0)
    {
        return Results.Json(
            new BatchResponse(accepted.ToArray(), "duplicate_event_id", "One or more event IDs were duplicates.", retryable: false),
            statusCode: StatusCodes.Status409Conflict);
    }

    return Results.Ok(new BatchResponse(accepted.ToArray(), null, null, retryable: null));
});

app.Run();

static string CreateToken() =>
    Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + Convert.ToBase64String(Guid.NewGuid().ToByteArray());

public sealed record MockEnrollmentRequest(
    string enrollment_code,
    string environment_name,
    string machine_name,
    DateTimeOffset requested_at_utc);

public sealed record MockEnrollmentResponse(
    string agent_id,
    string device_id,
    string tenant_binding,
    string ingestion_url,
    string site_timezone,
    string access_token,
    string refresh_token,
    DateTimeOffset credential_expiry_utc,
    DateTimeOffset refresh_token_expiry_utc,
    string token_refresh_url);

public sealed record MockTokenRefreshRequest(
    string agent_id,
    string device_id,
    string tenant_binding,
    string refresh_token,
    DateTimeOffset requested_at_utc);

public sealed record MockTokenRefreshResponse(
    string agent_id,
    string device_id,
    string tenant_binding,
    string token_type,
    string access_token,
    string refresh_token,
    DateTimeOffset credential_expiry_utc,
    DateTimeOffset refresh_token_expiry_utc);

public sealed record BatchRequest(string agentId, string tenantBinding, BatchEvent[] events);

public sealed record BatchEvent(
    string eventId,
    string eventType,
    string schemaVersion,
    DateTimeOffset eventTimestampUtc,
    DateTimeOffset observedTimestampUtc,
    string agentId,
    string workstationId,
    string tenantBinding,
    string sourceAdapter,
    string? correlationId,
    string environmentName);

public sealed record ErrorResponse(string errorCode, string message, bool retryable);

public sealed record BatchResponse(string[] acceptedEventIds, string? errorCode, string? message, bool? retryable = null);

public sealed record AccessTokenBinding(
    string AgentId,
    string DeviceId,
    string TenantBinding,
    DateTimeOffset ExpiryUtc);

public sealed record RefreshTokenBinding(
    string AgentId,
    string DeviceId,
    string TenantBinding,
    DateTimeOffset ExpiryUtc,
    bool Revoked);

public partial class Program
{
}

public sealed class MockAtlasApiMarker
{
}
