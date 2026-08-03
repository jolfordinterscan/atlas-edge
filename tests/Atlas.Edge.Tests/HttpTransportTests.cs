using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Atlas.Edge.Core;
using Atlas.Edge.Security;
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
    public async Task AuthenticatedScannerInventoryDelivery_UsesExistingBatchContract()
    {
        var inventory = CreateInventory();
        var transport = CreateTransport(new FakeHandler((request, _) =>
        {
            var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var sent = payload.RootElement.GetProperty("events")[0];
            Assert.Equal("scanner.inventory", sent.GetProperty("eventType").GetString());
            Assert.Equal(inventory.InventoryVersion, sent.GetProperty("inventoryVersion").GetString());
            var scanner = sent.GetProperty("scanners")[0];
            Assert.Equal("scanner-ca9cbc762608af46bece7e18", scanner.GetProperty("scannerId").GetString());
            Assert.Equal("FUJITSU", scanner.GetProperty("driverProvider").GetString());
            Assert.Equal("04C5", scanner.GetProperty("usbVendorId").GetString());
            Assert.Equal("15FF", scanner.GetProperty("usbProductId").GetString());
            Assert.Equal(new string('c', 64), scanner.GetProperty("containerId").GetString());
            Assert.Equal(new string('b', 64), scanner.GetProperty("locationPathHash").GetString());
            Assert.False(scanner.TryGetProperty("devicePath", out var rawDevicePath));
            Assert.False(scanner.TryGetProperty("deviceInstanceId", out var rawDeviceInstanceId));
            Assert.Equal(JsonValueKind.Undefined, rawDeviceInstanceId.ValueKind);
            Assert.Equal(JsonValueKind.Undefined, rawDevicePath.ValueKind);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"acceptedEventIds\":[\"{inventory.EventId}\"]}}", Encoding.UTF8, "application/json")
            };
        }));

        var result = await transport.SendInventoryAsync(inventory, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(inventory.EventId, result.AcceptedEventIds);
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
        Assert.Equal(TransportFailureKind.AuthenticationRequired, result.FailureKind);
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
        Assert.Equal("https_required", result.Error);
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

    private static ScannerInventoryEvent CreateInventory()
    {
        var entry = new ScannerInventoryEntry(
                "scanner-ca9cbc762608af46bece7e18",
                "wia",
                "WIA",
                "FUJITSU",
                "fi-8170",
                null,
                new string('a', 64),
                "fi-8170",
                "2.0.0.9",
                "Wia",
                "Usb",
                null,
                "Unknown",
                true,
                ["Unknown"],
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow,
                "ProviderStableIdentity",
                [])
        {
            DriverProvider = "FUJITSU",
            UsbVendorId = "04C5",
            UsbProductId = "15FF",
            ContainerId = new string('c', 64),
            LocationPathHash = new string('b', 64),
            DeviceInstanceIdHash = new string('d', 64)
        };
        return new(
            "inventory-event",
            "scanner.inventory",
            "1.0",
            DateTimeOffset.UtcNow,
            "agent-1",
            "device-1",
            new string('a', 64),
            1,
            [entry]);
    }

    private sealed class StaticCredentialProvider : ITransportCredentialProvider
    {
        private readonly string _ingestionUrl;

        public StaticCredentialProvider(string ingestionUrl)
        {
            _ingestionUrl = ingestionUrl;
        }

        public CredentialLifecycleState State => CredentialLifecycleState.Active;

        public ValueTask<CredentialLeaseResult> GetLeaseAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(CredentialLeaseResult.Available(CreateLease(generation: 1)));

        public ValueTask<CredentialLeaseResult> RefreshAfterAccessTokenExpiredAsync(
            long rejectedGeneration,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(CredentialLeaseResult.Available(CreateLease(generation: rejectedGeneration + 1)));

        public ValueTask<CredentialLeaseResult> InvalidateAfterAuthenticationFailureAsync(
            long rejectedGeneration,
            string errorCode,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(CredentialLeaseResult.Unavailable(
                CredentialAvailabilityKind.AuthenticationRequired,
                errorCode));

        private CredentialLease CreateLease(long generation) =>
            new(
                "agent-1",
                "device-1",
                "tenant-a",
                _ingestionUrl,
                "UTC",
                "token-123",
                "refresh-123",
                DateTimeOffset.UtcNow.AddHours(1),
                DateTimeOffset.UtcNow.AddDays(1),
                "https://localhost:7143/api/edge/v1/token/refresh",
                generation,
                "Test",
                DateTimeOffset.UtcNow);
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
