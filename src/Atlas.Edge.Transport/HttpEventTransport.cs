using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Atlas.Edge.Core;
using Atlas.Edge.Security;
using Microsoft.Extensions.Logging;

namespace Atlas.Edge.Transport;

public sealed class HttpEventTransport : IEventTransport
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly ITransportCredentialProvider _credentialProvider;
    private readonly EndpointSecurityPolicy _endpointSecurityPolicy;
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpEventTransport> _logger;

    public HttpEventTransport(
        HttpClient httpClient,
        ITransportCredentialProvider credentialProvider,
        EndpointSecurityPolicy endpointSecurityPolicy,
        ILogger<HttpEventTransport> logger)
    {
        _httpClient = httpClient;
        _credentialProvider = credentialProvider;
        _endpointSecurityPolicy = endpointSecurityPolicy;
        _logger = logger;
    }

    public async Task<TransportSendResult> SendAsync(
        IReadOnlyList<QueueItem<AgentHeartbeatEvent>> batch,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (batch.Count == 0)
        {
            return TransportSendResult.Success(Array.Empty<string>());
        }

        var leaseResult = await _credentialProvider.GetLeaseAsync(cancellationToken);
        if (leaseResult.Lease is null)
        {
            return MapCredentialFailure(leaseResult);
        }

        var firstAttempt = await SendOnceAsync(batch, leaseResult.Lease, cancellationToken);
        if (!firstAttempt.AccessTokenExpired)
        {
            return firstAttempt.Result;
        }

        var refreshed = await _credentialProvider.RefreshAfterAccessTokenExpiredAsync(
            leaseResult.Lease.Generation,
            cancellationToken);
        if (refreshed.Lease is null)
        {
            return MapCredentialFailure(refreshed);
        }

        var replay = await SendOnceAsync(batch, refreshed.Lease, cancellationToken);
        return replay.AccessTokenExpired
            ? TransportSendResult.Retryable("access_token_expired_after_refresh")
            : replay.Result;
    }

    private async Task<SendAttempt> SendOnceAsync(
        IReadOnlyList<QueueItem<AgentHeartbeatEvent>> batch,
        CredentialLease lease,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(lease.IngestionUrl, UriKind.Absolute, out var endpoint))
        {
            return SendAttempt.Completed(TransportSendResult.NonRetryable("invalid_transport_endpoint"));
        }

        if (!_endpointSecurityPolicy.IsAllowed(endpoint))
        {
            return SendAttempt.Completed(TransportSendResult.NonRetryable("https_required"));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, "/api/edge/v1/events/batch"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", lease.AccessToken);

        var correlationId = Guid.NewGuid().ToString("N");
        request.Headers.Add("x-correlation-id", correlationId);
        request.Content = JsonContent.Create(new BatchRequest(
            lease.AgentId,
            lease.TenantBinding,
            batch.Select(item => item.Payload).ToArray()), options: SerializerOptions);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout)
            {
                return SendAttempt.Completed(TransportSendResult.Retryable($"http_{(int)response.StatusCode}"));
            }

            BatchResponse? body;
            try
            {
                body = await response.Content.ReadFromJsonAsync<BatchResponse>(SerializerOptions, cancellationToken);
            }
            catch (JsonException)
            {
                body = null;
            }

            var acceptedEventIds = body?.AcceptedEventIds ?? Array.Empty<string>();
            var errorCode = body?.ErrorCode ?? $"http_{(int)response.StatusCode}";

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "HTTP transport accepted {AcceptedCount} events with correlation ID {CorrelationId} using credential generation {Generation}.",
                    acceptedEventIds.Length,
                    correlationId,
                    lease.Generation);
                return SendAttempt.Completed(TransportSendResult.Success(acceptedEventIds));
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized &&
                string.Equals(errorCode, "access_token_expired", StringComparison.Ordinal))
            {
                return SendAttempt.Expired();
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                var invalidated = await _credentialProvider.InvalidateAfterAuthenticationFailureAsync(
                    lease.Generation,
                    errorCode,
                    cancellationToken);
                return invalidated.Lease is null
                    ? SendAttempt.Completed(TransportSendResult.AuthenticationRequired(errorCode))
                    : SendAttempt.Completed(TransportSendResult.Retryable("stale_authentication_failure"));
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                return SendAttempt.Completed(TransportSendResult.NonRetryable(errorCode, acceptedEventIds));
            }

            if (response.StatusCode == HttpStatusCode.Conflict &&
                string.Equals(errorCode, "duplicate_event_id", StringComparison.Ordinal))
            {
                return SendAttempt.Completed(TransportSendResult.Success(batch.Select(item => item.Payload.EventId)));
            }

            return SendAttempt.Completed(TransportSendResult.Retryable(errorCode, acceptedEventIds));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SendAttempt.Completed(TransportSendResult.Retryable("transport_timeout"));
        }
        catch (HttpRequestException)
        {
            return SendAttempt.Completed(TransportSendResult.Retryable("transport_network_error"));
        }
    }

    private static TransportSendResult MapCredentialFailure(CredentialLeaseResult result) =>
        result.Kind == CredentialAvailabilityKind.AuthenticationRequired
            ? TransportSendResult.AuthenticationRequired(result.ErrorCode ?? "authentication_required")
            : TransportSendResult.Retryable(result.ErrorCode ?? "credential_unavailable");

    private sealed record BatchRequest(string AgentId, string TenantBinding, IReadOnlyList<AgentHeartbeatEvent> Events);

    private sealed record BatchResponse(string[] AcceptedEventIds, string? ErrorCode, string? Message);

    private sealed record SendAttempt(TransportSendResult Result, bool AccessTokenExpired)
    {
        public static SendAttempt Completed(TransportSendResult result) => new(result, false);

        public static SendAttempt Expired() => new(TransportSendResult.Retryable("access_token_expired"), true);
    }
}
