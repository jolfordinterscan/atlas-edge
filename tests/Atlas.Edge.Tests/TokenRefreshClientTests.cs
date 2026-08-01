using System.Net;
using System.Text;
using Atlas.Edge.Core;
using Atlas.Edge.Enrollment;

namespace Atlas.Edge.Tests;

public sealed class TokenRefreshClientTests
{
    [Fact]
    public async Task Refresh_SuccessValidatesBindingAndExpiry()
    {
        var now = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var response = "{" +
            "\"agent_id\":\"agent-1\"," +
            "\"device_id\":\"device-1\"," +
            "\"tenant_binding\":\"tenant-a\"," +
            "\"token_type\":\"Bearer\"," +
            "\"access_token\":\"rotated-access\"," +
            "\"refresh_token\":\"rotated-refresh\"," +
            "\"credential_expiry_utc\":\"2030-01-01T01:00:00Z\"," +
            "\"refresh_token_expiry_utc\":\"2030-01-02T00:00:00Z\"" +
            "}";
        var client = CreateClient(new StaticHandler(HttpStatusCode.OK, response), now);

        var result = await client.RefreshAsync(
            new Uri("https://localhost:7143/api/edge/v1/token/refresh"),
            CreateRequest(now),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("rotated-access", result.Response!.AccessToken);
    }

    [Fact]
    public async Task RefreshFailure_DoesNotExposeRawResponseBody()
    {
        const string secretMarker = "raw-refresh-token-must-not-escape";
        var client = CreateClient(
            new StaticHandler(HttpStatusCode.ServiceUnavailable, $"{{\"message\":\"{secretMarker}\"}}"),
            DateTimeOffset.UtcNow);

        var result = await client.RefreshAsync(
            new Uri("https://localhost:7143/api/edge/v1/token/refresh"),
            CreateRequest(DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.Equal(TokenRefreshFailureKind.Transient, result.FailureKind);
        Assert.DoesNotContain(secretMarker, result.ErrorCode, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refresh_RejectsInsecureEndpoint()
    {
        var client = CreateClient(new StaticHandler(HttpStatusCode.OK, "{}"), DateTimeOffset.UtcNow);

        var result = await client.RefreshAsync(
            new Uri("http://localhost:5143/api/edge/v1/token/refresh"),
            CreateRequest(DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.Equal(TokenRefreshFailureKind.EndpointRejected, result.FailureKind);
        Assert.Equal("https_required", result.ErrorCode);
    }

    private static HttpTokenRefreshClient CreateClient(HttpMessageHandler handler, DateTimeOffset now) =>
        new(
            new HttpClient(handler),
            new EndpointSecurityPolicy(allowInsecureHttp: false),
            new ManualTimeProvider(now));

    private static TokenRefreshRequest CreateRequest(DateTimeOffset now) =>
        new("agent-1", "device-1", "tenant-a", "refresh-token", now);

    private sealed class StaticHandler : HttpMessageHandler
    {
        private readonly string _body;
        private readonly HttpStatusCode _statusCode;

        public StaticHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
    }
}
