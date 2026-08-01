using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Atlas.Edge.Tests;

public sealed class MockAtlasTokenRefreshTests
{
    [Fact]
    public async Task Refresh_RotatesTokens_AndRejectsReuse()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var enrollment = await EnrollAsync(client);

        var first = await RefreshAsync(client, enrollment);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var rotated = await ReadJsonAsync(first);
        Assert.NotEqual(enrollment.AccessToken, rotated.RootElement.GetProperty("access_token").GetString());
        Assert.NotEqual(enrollment.RefreshToken, rotated.RootElement.GetProperty("refresh_token").GetString());

        var reused = await RefreshAsync(client, enrollment);
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
        Assert.Contains("refresh_token_reused", await reused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refresh_RejectsRevokedExpiredAndBindingMismatchedTokens()
    {
        await using (var revokedFactory = CreateFactory(("MockAtlas:RevokeIssuedRefreshTokens", "true")))
        {
            var client = revokedFactory.CreateClient();
            var enrollment = await EnrollAsync(client);
            var response = await RefreshAsync(client, enrollment);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        await using (var expiredFactory = CreateFactory(("MockAtlas:RefreshTokenTtlSeconds", "0")))
        {
            var client = expiredFactory.CreateClient();
            var enrollment = await EnrollAsync(client);
            var response = await RefreshAsync(client, enrollment);
            Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        }

        await using (var mismatchFactory = CreateFactory())
        {
            var client = mismatchFactory.CreateClient();
            var enrollment = await EnrollAsync(client);
            var response = await RefreshAsync(client, enrollment with { TenantBinding = "other-tenant" });
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }
    }

    [Fact]
    public async Task Events_ReturnAccessTokenExpired_ForExpiredToken()
    {
        await using var factory = CreateFactory(("MockAtlas:AccessTokenTtlSeconds", "0"));
        var client = factory.CreateClient();
        var enrollment = await EnrollAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", enrollment.AccessToken);

        var response = await client.PostAsJsonAsync("/api/edge/v1/events/batch", new
        {
            agentId = enrollment.AgentId,
            tenantBinding = enrollment.TenantBinding,
            events = new[]
            {
                new
                {
                    eventId = "event-1",
                    eventType = "agent.heartbeat",
                    schemaVersion = "1.0",
                    eventTimestampUtc = DateTimeOffset.UtcNow,
                    observedTimestampUtc = DateTimeOffset.UtcNow,
                    agentId = enrollment.AgentId,
                    workstationId = enrollment.DeviceId,
                    tenantBinding = enrollment.TenantBinding,
                    sourceAdapter = "runtime.foundation",
                    correlationId = (string?)null,
                    environmentName = "Test"
                }
            }
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("access_token_expired", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private static WebApplicationFactory<MockAtlasApiMarker> CreateFactory(
        params (string Key, string Value)[] overrides) =>
        new WebApplicationFactory<MockAtlasApiMarker>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var values = new Dictionary<string, string?>
                {
                    ["MockAtlas:DevelopmentEnrollmentCode"] = "TEST-LOCAL-CODE",
                    ["MockAtlas:TokenRefreshUrl"] = "https://localhost:7143/api/edge/v1/token/refresh"
                };
                foreach (var (key, value) in overrides)
                {
                    values[key] = value;
                }

                configuration.AddInMemoryCollection(values);
            });
        });

    private static async Task<EnrollmentSnapshot> EnrollAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/edge/v1/enroll", new
        {
            enrollment_code = "TEST-LOCAL-CODE",
            environment_name = "Test",
            machine_name = "test-machine",
            requested_at_utc = DateTimeOffset.UtcNow
        });
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        return new EnrollmentSnapshot(
            json.RootElement.GetProperty("agent_id").GetString()!,
            json.RootElement.GetProperty("device_id").GetString()!,
            json.RootElement.GetProperty("tenant_binding").GetString()!,
            json.RootElement.GetProperty("access_token").GetString()!,
            json.RootElement.GetProperty("refresh_token").GetString()!);
    }

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, EnrollmentSnapshot enrollment) =>
        client.PostAsJsonAsync("/api/edge/v1/token/refresh", new
        {
            agent_id = enrollment.AgentId,
            device_id = enrollment.DeviceId,
            tenant_binding = enrollment.TenantBinding,
            refresh_token = enrollment.RefreshToken,
            requested_at_utc = DateTimeOffset.UtcNow
        });

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private sealed record EnrollmentSnapshot(
        string AgentId,
        string DeviceId,
        string TenantBinding,
        string AccessToken,
        string RefreshToken);
}
