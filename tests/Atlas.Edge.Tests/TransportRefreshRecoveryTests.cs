using System.Net;
using System.Text;
using Atlas.Edge.Core;
using Atlas.Edge.Security;
using Atlas.Edge.Transport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atlas.Edge.Tests;

public sealed class TransportRefreshRecoveryTests
{
    [Fact]
    public async Task AccessTokenExpired_RefreshesAndReplaysExactlyOnce()
    {
        var provider = new RotatingProvider();
        var requests = 0;
        var handler = new CallbackHandler(request =>
        {
            requests++;
            var token = request.Headers.Authorization!.Parameter;
            return token == "old-access"
                ? Response(HttpStatusCode.Unauthorized, "{\"errorCode\":\"access_token_expired\"}")
                : Response(HttpStatusCode.OK, "{\"acceptedEventIds\":[\"event-1\"]}");
        });
        var transport = CreateTransport(handler, provider);

        var result = await transport.SendAsync(new[] { CreateItem("event-1") }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, requests);
        Assert.Equal(1, provider.RefreshCalls);
    }

    [Fact]
    public async Task RepeatedAccessTokenExpired_DoesNotLoop()
    {
        var provider = new RotatingProvider();
        var requests = 0;
        var transport = CreateTransport(new CallbackHandler(_ =>
        {
            requests++;
            return Response(HttpStatusCode.Unauthorized, "{\"errorCode\":\"access_token_expired\"}");
        }), provider);

        var result = await transport.SendAsync(new[] { CreateItem("event-1") }, CancellationToken.None);

        Assert.Equal(TransportFailureKind.Retryable, result.FailureKind);
        Assert.Equal(2, requests);
        Assert.Equal(1, provider.RefreshCalls);
    }

    [Fact]
    public async Task GenericUnauthorized_DoesNotRefresh()
    {
        var provider = new RotatingProvider();
        var transport = CreateTransport(
            new CallbackHandler(_ => Response(HttpStatusCode.Unauthorized, "{\"errorCode\":\"invalid_access_token\"}")),
            provider);

        var result = await transport.SendAsync(new[] { CreateItem("event-1") }, CancellationToken.None);

        Assert.Equal(TransportFailureKind.AuthenticationRequired, result.FailureKind);
        Assert.Equal(0, provider.RefreshCalls);
    }

    private static HttpEventTransport CreateTransport(HttpMessageHandler handler, ITransportCredentialProvider provider) =>
        new(
            new HttpClient(handler),
            provider,
            new EndpointSecurityPolicy(allowInsecureHttp: false),
            NullLogger<HttpEventTransport>.Instance);

    private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static QueueItem<AgentHeartbeatEvent> CreateItem(string eventId) =>
        new(
            "receipt-1",
            new AgentHeartbeatEvent(
                eventId,
                "agent.heartbeat",
                "1.0",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                "agent-1",
                "device-1",
                "tenant-a",
                "runtime.foundation",
                null,
                "Test"),
            0,
            DateTimeOffset.UtcNow,
            null,
            null);

    private sealed class RotatingProvider : ITransportCredentialProvider
    {
        public int RefreshCalls { get; private set; }

        public CredentialLifecycleState State => CredentialLifecycleState.Active;

        public ValueTask<CredentialLeaseResult> GetLeaseAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(CredentialLeaseResult.Available(CreateLease("old-access", 1)));

        public ValueTask<CredentialLeaseResult> RefreshAfterAccessTokenExpiredAsync(
            long rejectedGeneration,
            CancellationToken cancellationToken)
        {
            RefreshCalls++;
            return ValueTask.FromResult(CredentialLeaseResult.Available(CreateLease("new-access", 2)));
        }

        public ValueTask<CredentialLeaseResult> InvalidateAfterAuthenticationFailureAsync(
            long rejectedGeneration,
            string errorCode,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(CredentialLeaseResult.Unavailable(
                CredentialAvailabilityKind.AuthenticationRequired,
                errorCode));

        private static CredentialLease CreateLease(string accessToken, long generation) =>
            new(
                "agent-1",
                "device-1",
                "tenant-a",
                "https://localhost:7143/",
                "UTC",
                accessToken,
                "refresh-token",
                DateTimeOffset.UtcNow.AddHours(1),
                DateTimeOffset.UtcNow.AddDays(1),
                "https://localhost:7143/api/edge/v1/token/refresh",
                generation,
                "Test",
                DateTimeOffset.UtcNow);
    }

    private sealed class CallbackHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _callback;

        public CallbackHandler(Func<HttpRequestMessage, HttpResponseMessage> callback)
        {
            _callback = callback;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_callback(request));
    }
}
