using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables("ATLAS_MOCK_");

var app = builder.Build();

var mockConfig = builder.Configuration.GetSection("MockAtlas");
var expectedCode = mockConfig["DevelopmentEnrollmentCode"] ?? "SET_VIA_ATLAS_MOCK_MockAtlas__DevelopmentEnrollmentCode";
var tenantBinding = mockConfig["TenantBinding"] ?? "tenant-dev-a";
var ingestionUrl = mockConfig["IngestionUrl"] ?? "https://localhost:7143/";
var siteTimezone = mockConfig["SiteTimezone"] ?? "UTC";
var ttlMinutes = int.TryParse(mockConfig["AccessTokenTtlMinutes"], out var parsedTtl) && parsedTtl > 0 ? parsedTtl : 60;

var usedEnrollmentCodes = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
var tokenBindings = new ConcurrentDictionary<string, (string AgentId, string TenantBinding)>(StringComparer.Ordinal);
var seenEventIds = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    now_utc = DateTimeOffset.UtcNow
}));

app.MapPost("/api/edge/v1/enroll", (EnrollmentRequest request, ILoggerFactory loggerFactory) =>
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
    var accessToken = Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + Convert.ToBase64String(Guid.NewGuid().ToByteArray());
    var refreshToken = "refresh-token-placeholder";
    var expiryUtc = DateTimeOffset.UtcNow.AddMinutes(ttlMinutes);

    tokenBindings[accessToken] = (agentId, tenantBinding);

    logger.LogInformation("Enrollment accepted for agent {AgentId} and machine {MachineName}.", agentId, request.machine_name);

    return Results.Ok(new EnrollmentResponse(
        agent_id: agentId,
        device_id: deviceId,
        tenant_binding: tenantBinding,
        ingestion_url: ingestionUrl,
        site_timezone: siteTimezone,
        access_token: accessToken,
        refresh_token: refreshToken,
        credential_expiry_utc: expiryUtc));
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
    if (string.IsNullOrWhiteSpace(token) || !tokenBindings.TryGetValue(token, out var binding))
    {
        return Results.Json(
            new ErrorResponse("unauthorized", "Bearer token is invalid.", retryable: false),
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

public sealed record EnrollmentRequest(
    string enrollment_code,
    string environment_name,
    string machine_name,
    DateTimeOffset requested_at_utc);

public sealed record EnrollmentResponse(
    string agent_id,
    string device_id,
    string tenant_binding,
    string ingestion_url,
    string site_timezone,
    string access_token,
    string refresh_token,
    DateTimeOffset credential_expiry_utc);

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

public partial class Program
{
}
