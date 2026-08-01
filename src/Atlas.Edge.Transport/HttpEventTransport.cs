using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Atlas.Edge.Core;
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

        var context = _credentialProvider.GetCurrent();
        if (context is null)
        {
            return TransportSendResult.Retryable("No transport credential context is available.");
        }

        if (!Uri.TryCreate(context.IngestionUrl, UriKind.Absolute, out var endpoint))
        {
            return TransportSendResult.NonRetryable("Transport endpoint is not a valid absolute URI.");
        }

        if (!_endpointSecurityPolicy.IsAllowed(endpoint))
        {
            return TransportSendResult.NonRetryable("Transport endpoint must use HTTPS.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, "/api/edge/v1/events/batch"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);

        var correlationId = Guid.NewGuid().ToString("N");
        request.Headers.Add("x-correlation-id", correlationId);

        request.Content = JsonContent.Create(new BatchRequest(
            context.AgentId,
            context.TenantBinding,
            batch.Select(item => item.Payload).ToArray()), options: SerializerOptions);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout)
            {
                return TransportSendResult.Retryable($"Transport failed with status {(int)response.StatusCode}.");
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

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "HTTP transport accepted {AcceptedCount} events with correlation ID {CorrelationId}.",
                    acceptedEventIds.Length,
                    correlationId);
                return TransportSendResult.Success(acceptedEventIds);
            }

            var errorCode = body?.ErrorCode ?? $"http_{(int)response.StatusCode}";
            var errorMessage = body?.Message ?? "Transport request failed.";
            var combined = $"{errorCode}: {errorMessage}";

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest)
            {
                return TransportSendResult.NonRetryable(combined, acceptedEventIds);
            }

            if (response.StatusCode == HttpStatusCode.Conflict &&
                string.Equals(errorCode, "duplicate_event_id", StringComparison.Ordinal))
            {
                return TransportSendResult.Success(batch.Select(item => item.Payload.EventId));
            }

            return TransportSendResult.Retryable(combined, acceptedEventIds);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return TransportSendResult.Retryable("Transport request timed out.");
        }
        catch (HttpRequestException ex)
        {
            return TransportSendResult.Retryable($"Transport request failed: {ex.Message}");
        }
    }

    private sealed record BatchRequest(string AgentId, string TenantBinding, IReadOnlyList<AgentHeartbeatEvent> Events);

    private sealed record BatchResponse(string[] AcceptedEventIds, string? ErrorCode, string? Message);
}
