using System.Net;
using System.Net.Http;
using System.Text;
using Atlas.Edge.Core;
using Atlas.Edge.Transport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atlas.Edge.Tests;

public sealed class HttpTransportTests
{
    [Fact]
    public async Task AuthenticatedHeartbeatDelivery_Succeeds()
    {
        var eventId = Guid.NewGuid().ToString("N");
        var transport = CreateTransport(new FakeHandler((request, _) =>
        {
            Assert.NotNull(request.Headers.Authorization);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("token-123", request.Headers.Authorization.Parameter);
            Assert.True(request.Headers.Contains("x-correlation-id"));

            var body = "{\"acceptedEventIds\":[\"" + eventId + "\"]}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }));

        var result = await transport.SendAsync(new[] { CreateItem(eventId) }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(eventId, result.AcceptedEventIds);
    }

    [Fact]
    public async Task UnauthorizedHeartbeat_IsNonRetryable()
    {
        var transport = CreateTransport(new FakeHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"errorCode\":\"unauthorized\",\"message\":\"bad token\"}", Encoding.UTF8, "application/json")
            }));

        var result = await transport.SendAsync(new[] { CreateItem(Guid.NewGuid().ToString("N")) }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(TransportFailureKind.NonRetryable, result.FailureKind);
    }

    [Fact]
    public async Task DuplicateEventConflict_IsTreatedAsIdempotentSuccess()
    {
        var eventId = Guid.NewGuid().ToString("N");
        var transport = CreateTransport(new FakeHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent("{\"acceptedEventIds\":[],\"errorCode\":\"duplicate_event_id\",\"message\":\"duplicate\"}", Encoding.UTF8, "application/json")
            }));

        var result = await transport.SendAsync(new[] { CreateItem(eventId) }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(eventId, result.AcceptedEventIds);
    }

    [Fact]
    public async Task UnrelatedConflict_IsNotTreatedAsSuccess()
    {
        var transport = CreateTransport(new FakeHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent("{\"acceptedEventIds\":[],\"errorCode\":\"binding_conflict\",\"message\":\"conflict\"}", Encoding.UTF8, "application/json")
            }));

        var result = await transport.SendAsync(new[] { CreateItem(Guid.NewGuid().ToString("N")) }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(TransportFailureKind.Retryable, result.FailureKind);
    }

    [Fact]
    public async Task HttpEndpoint_IsRejected_WithoutDevelopmentOverride()
    {
        var transport = CreateTransport(
            new FakeHandler((_, _) => throw new InvalidOperationException("HTTP request should not be sent.")),
            ingestionUrl: "http://localhost:5143/");

        var result = await transport.SendAsync(new[] { CreateItem(Guid.NewGuid().ToString("N")) }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(TransportFailureKind.NonRetryable, result.FailureKind);
        Assert.Contains("HTTPS", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HttpEndpoint_IsAllowed_WithDevelopmentOverride()
    {
        var eventId = Guid.NewGuid().ToString("N");
        var transport = CreateTransport(
            new FakeHandler((_, _) =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"{{\"acceptedEventIds\":[\"{eventId}\"]}}", Encoding.UTF8, "application/json")
                }),
            ingestionUrl: "http://localhost:5143/",
            allowInsecureHttp: true);

        var result = await transport.SendAsync(new[] { CreateItem(eventId) }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(eventId, result.AcceptedEventIds);
    }

    [Fact]
    public async Task Http5xx_IsRetryable()
    {
        var transport = CreateTransport(new FakeHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            }));

        var result = await transport.SendAsync(new[] { CreateItem(Guid.NewGuid().ToString("N")) }, CancellationToken.None);

        Assert.Equal(TransportFailureKind.Retryable, result.FailureKind);
    }

    [Fact]
    public async Task Http400_IsNonRetryable()
    {
        var transport = CreateTransport(new FakeHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"errorCode\":\"invalid\",\"message\":\"bad request\"}", Encoding.UTF8, "application/json")
            }));

        var result = await transport.SendAsync(new[] { CreateItem(Guid.NewGuid().ToString("N")) }, CancellationToken.None);

        Assert.Equal(TransportFailureKind.NonRetryable, result.FailureKind);
    }

    private static HttpEventTransport CreateTransport(
        HttpMessageHandler handler,
        string ingestionUrl = "https://localhost:7143/",
        bool allowInsecureHttp = false)
    {
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(2)
        };

        return new HttpEventTransport(
            client,
            new StaticCredentialProvider(ingestionUrl),
            new EndpointSecurityPolicy(allowInsecureHttp),
            NullLogger<HttpEventTransport>.Instance);
    }

    private static QueueItem<AgentHeartbeatEvent> CreateItem(string eventId)
    {
        var evt = new AgentHeartbeatEvent(
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
            "Test");

        return new QueueItem<AgentHeartbeatEvent>(
            Guid.NewGuid().ToString("N"),
            evt,
            0,
            DateTimeOffset.UtcNow,
            null,
            null);
    }

    private sealed class StaticCredentialProvider : ITransportCredentialProvider
    {
        private readonly string _ingestionUrl;

        public StaticCredentialProvider(string ingestionUrl)
        {
            _ingestionUrl = ingestionUrl;
        }

        public TransportCredentialContext? GetCurrent() =>
            new(_ingestionUrl, "agent-1", "tenant-a", "token-123");
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _handler;

        public FakeHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request, cancellationToken));
        }
    }
}
