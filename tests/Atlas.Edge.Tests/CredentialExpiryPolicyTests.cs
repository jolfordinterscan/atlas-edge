using Atlas.Edge.Configuration;
using Atlas.Edge.Security;
using Microsoft.Extensions.Options;

namespace Atlas.Edge.Tests;

public sealed class CredentialExpiryPolicyTests
{
    [Fact]
    public void ExpiryPolicy_UsesRefreshWindowAndClockSkew()
    {
        var now = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new ManualTimeProvider(now);
        var policy = new CredentialExpiryPolicy(
            Options.Create(new AtlasEdgeOptions
            {
                TokenRefreshLeadTimeSeconds = 300,
                TokenClockSkewSeconds = 30
            }),
            timeProvider);
        var lease = CreateLease(now.AddMinutes(10), now.AddDays(1));

        Assert.False(policy.IsRefreshDue(lease));
        Assert.True(policy.CanUseAccessToken(lease));

        timeProvider.Advance(TimeSpan.FromMinutes(5));
        Assert.True(policy.IsRefreshDue(lease));

        timeProvider.Advance(TimeSpan.FromMinutes(4.5));
        Assert.False(policy.CanUseAccessToken(lease));
    }

    [Fact]
    public void RetryDelay_IsExponentialAndCapped()
    {
        var policy = new CredentialExpiryPolicy(
            Options.Create(new AtlasEdgeOptions
            {
                TokenRefreshRetryBaseSeconds = 2,
                TokenRefreshRetryMaxSeconds = 10
            }),
            new ManualTimeProvider(DateTimeOffset.UtcNow));

        Assert.Equal(TimeSpan.FromSeconds(2), policy.GetRetryDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(8), policy.GetRetryDelay(3));
        Assert.Equal(TimeSpan.FromSeconds(10), policy.GetRetryDelay(10));
    }

    private static CredentialLease CreateLease(DateTimeOffset accessExpiry, DateTimeOffset refreshExpiry) =>
        new(
            "agent-1",
            "device-1",
            "tenant-a",
            "https://localhost:7143/",
            "UTC",
            "access-token",
            "refresh-token",
            accessExpiry,
            refreshExpiry,
            "https://localhost:7143/api/edge/v1/token/refresh",
            1,
            "Test",
            DateTimeOffset.UtcNow);
}
