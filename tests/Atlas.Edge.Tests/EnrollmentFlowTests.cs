using System.Net;
using System.Net.Http;
using System.Text;
using Atlas.Edge.Core;
using Atlas.Edge.Enrollment;

namespace Atlas.Edge.Tests;

public sealed class EnrollmentFlowTests
{
    [Fact]
    public async Task Enrollment_ReturnsIdentity_ForValidCode()
    {
        var handler = new EnrollmentHandler();
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://localhost:7143/")
        };

        var enrollmentClient = new HttpEnrollmentClient(client, new EndpointSecurityPolicy(allowInsecureHttp: false));

        var result = await enrollmentClient.EnrollAsync(new EnrollmentRequest(
            "DEV-CODE-VALID",
            "Test",
            "test-machine",
            DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.NotNull(result.Response);
        Assert.False(result.IsRetryable);
        Assert.StartsWith("agent-", result.Response!.AgentId, StringComparison.Ordinal);
        Assert.StartsWith("device-", result.Response.DeviceId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Enrollment_Fails_ForInvalidCode()
    {
        var handler = new EnrollmentHandler();
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://localhost:7143/")
        };

        var enrollmentClient = new HttpEnrollmentClient(client, new EndpointSecurityPolicy(allowInsecureHttp: false));

        var result = await enrollmentClient.EnrollAsync(new EnrollmentRequest(
            "INVALID-CODE",
            "Test",
            "test-machine",
            DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.Null(result.Response);
        Assert.False(result.IsRetryable);
        Assert.Contains("400", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("INVALID-CODE", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Enrollment_Fails_WhenCodeIsReused()
    {
        var handler = new EnrollmentHandler();
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://localhost:7143/")
        };

        var enrollmentClient = new HttpEnrollmentClient(client, new EndpointSecurityPolicy(allowInsecureHttp: false));

        var first = await enrollmentClient.EnrollAsync(new EnrollmentRequest(
            "DEV-CODE-REUSE",
            "Test",
            "test-machine",
            DateTimeOffset.UtcNow),
            CancellationToken.None);

        var second = await enrollmentClient.EnrollAsync(new EnrollmentRequest(
            "DEV-CODE-REUSE",
            "Test",
            "test-machine-2",
            DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.NotNull(first.Response);
        Assert.Null(second.Response);
        Assert.False(second.IsRetryable);
        Assert.Contains("409", second.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Enrollment_RejectsHttpEndpoint_WithoutDevelopmentOverride()
    {
        var client = new HttpClient(new EnrollmentHandler())
        {
            BaseAddress = new Uri("http://localhost:5143/")
        };
        var enrollmentClient = new HttpEnrollmentClient(client, new EndpointSecurityPolicy(allowInsecureHttp: false));

        var result = await enrollmentClient.EnrollAsync(new EnrollmentRequest(
            "DEV-CODE-VALID",
            "Test",
            "test-machine",
            DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.Null(result.Response);
        Assert.False(result.IsRetryable);
        Assert.Contains("HTTPS", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Enrollment_AllowsHttpEndpoint_WithDevelopmentOverride()
    {
        var client = new HttpClient(new EnrollmentHandler(ingestionUrl: "http://localhost:5143/"))
        {
            BaseAddress = new Uri("http://localhost:5143/")
        };
        var enrollmentClient = new HttpEnrollmentClient(client, new EndpointSecurityPolicy(allowInsecureHttp: true));

        var result = await enrollmentClient.EnrollAsync(new EnrollmentRequest(
            "DEV-CODE-VALID",
            "Development",
            "test-machine",
            DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.NotNull(result.Response);
    }

    private sealed class EnrollmentHandler : HttpMessageHandler
    {
        private readonly HashSet<string> _usedCodes = new(StringComparer.Ordinal);
        private readonly string _ingestionUrl;

        public EnrollmentHandler(string ingestionUrl = "https://localhost:7143/")
        {
            _ingestionUrl = ingestionUrl;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var body = request.Content!.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();

            if (body.Contains("INVALID-CODE", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{\"errorCode\":\"invalid_enrollment_code\",\"message\":\"INVALID-CODE\"}", Encoding.UTF8, "application/json")
                });
            }

            if (body.Contains("DEV-CODE-REUSE", StringComparison.Ordinal) && !_usedCodes.Add("DEV-CODE-REUSE"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = new StringContent("{\"errorCode\":\"enrollment_code_reused\"}", Encoding.UTF8, "application/json")
                });
            }

            var response = "{" +
                "\"agent_id\":\"agent-123\"," +
                "\"device_id\":\"device-123\"," +
                "\"tenant_binding\":\"tenant-a\"," +
                $"\"ingestion_url\":\"{_ingestionUrl}\"," +
                "\"site_timezone\":\"UTC\"," +
                "\"access_token\":\"token-123\"," +
                "\"refresh_token\":\"refresh-token-placeholder\"," +
                "\"credential_expiry_utc\":\"2030-01-01T00:00:00Z\"" +
                "}";

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            });
        }
    }
}
