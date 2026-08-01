using Atlas.Edge.Configuration;
using Atlas.Edge.Core;
using Atlas.Edge.Enrollment;
using Atlas.Edge.Runtime;
using Atlas.Edge.Security;
using Atlas.Edge.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Atlas.Edge.Tests;

public sealed class CredentialRefreshCoordinatorTests
{
    [Fact]
    public async Task ConcurrentLeaseRequests_PerformSingleRefreshAndPersistBeforePublish()
    {
        var now = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new ManualTimeProvider(now);
        var store = new TrackingCredentialStore();
        var refreshClient = new StubRefreshClient(now);
        using var provider = CreateProvider(store, refreshClient, timeProvider);
        provider.Initialize(CreateCredentials(now.AddMinutes(2), now.AddDays(1)));

        var calls = Enumerable.Range(0, 20)
            .Select(_ => provider.GetLeaseAsync(CancellationToken.None).AsTask())
            .ToArray();
        var results = await Task.WhenAll(calls);

        Assert.Equal(1, refreshClient.Calls);
        Assert.Equal(1, store.SaveCalls);
        Assert.All(results, result => Assert.Equal(2, result.Lease!.Generation));
        Assert.Equal("rotated-access-token", store.Stored!.AccessToken);
        Assert.Equal(CredentialLifecycleState.Active, provider.State);
    }

    [Fact]
    public async Task RejectedStaleGeneration_DoesNotRefreshAgain()
    {
        var now = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new ManualTimeProvider(now);
        var refreshClient = new StubRefreshClient(now);
        using var provider = CreateProvider(new TrackingCredentialStore(), refreshClient, timeProvider);
        provider.Initialize(CreateCredentials(now.AddMinutes(2), now.AddDays(1)));

        var refreshed = await provider.GetLeaseAsync(CancellationToken.None);
        var staleRecovery = await provider.RefreshAfterAccessTokenExpiredAsync(1, CancellationToken.None);

        Assert.Equal(1, refreshClient.Calls);
        Assert.Equal(refreshed.Lease!.Generation, staleRecovery.Lease!.Generation);
    }

    [Fact]
    public async Task RevokedRefreshToken_TransitionsToAuthenticationRequiredAndDeletesStore()
    {
        var now = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var store = new TrackingCredentialStore();
        var refreshClient = new StubRefreshClient(now)
        {
            Result = TokenRefreshResult.Failure(TokenRefreshFailureKind.RevokedRefreshToken, 403, "refresh_token_revoked")
        };
        using var provider = CreateProvider(store, refreshClient, new ManualTimeProvider(now));
        provider.Initialize(CreateCredentials(now.AddMinutes(2), now.AddDays(1)));

        var result = await provider.GetLeaseAsync(CancellationToken.None);

        Assert.Equal(CredentialAvailabilityKind.AuthenticationRequired, result.Kind);
        Assert.Equal(CredentialLifecycleState.AuthenticationRequired, provider.State);
        Assert.Equal(1, store.DeleteCalls);
    }

    [Fact]
    public async Task TransientRefreshFailure_AfterSafeBoundaryDoesNotReturnExpiredLease()
    {
        var now = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var refreshClient = new StubRefreshClient(now)
        {
            Result = TokenRefreshResult.Failure(TokenRefreshFailureKind.Transient, 503, "http_503")
        };
        using var provider = CreateProvider(new TrackingCredentialStore(), refreshClient, new ManualTimeProvider(now));
        provider.Initialize(CreateCredentials(now.AddSeconds(20), now.AddDays(1)));

        var result = await provider.GetLeaseAsync(CancellationToken.None);

        Assert.Null(result.Lease);
        Assert.Equal(CredentialAvailabilityKind.RetryableUnavailable, result.Kind);
        Assert.Equal(CredentialLifecycleState.Refreshing, provider.State);
    }

    private static RuntimeTransportCredentialProvider CreateProvider(
        ICredentialStore store,
        ITokenRefreshClient refreshClient,
        TimeProvider timeProvider)
    {
        var policy = new CredentialExpiryPolicy(
            Options.Create(new AtlasEdgeOptions
            {
                TokenRefreshLeadTimeSeconds = 300,
                TokenClockSkewSeconds = 30,
                TokenRefreshRetryBaseSeconds = 2,
                TokenRefreshRetryMaxSeconds = 60
            }),
            timeProvider);
        return new RuntimeTransportCredentialProvider(
            store,
            refreshClient,
            policy,
            NullLogger<RuntimeTransportCredentialProvider>.Instance);
    }

    private static StoredEdgeCredentials CreateCredentials(
        DateTimeOffset accessExpiry,
        DateTimeOffset refreshExpiry) =>
        new(
            new AgentIdentity("agent-1", "device-1", "tenant-a", "Test", false, accessExpiry.AddHours(-1)),
            "device-1",
            "https://localhost:7143/",
            "UTC",
            "access-token",
            "refresh-token",
            accessExpiry,
            refreshExpiry,
            "https://localhost:7143/api/edge/v1/token/refresh",
            1,
            accessExpiry.AddHours(-1));

    private sealed class StubRefreshClient : ITokenRefreshClient
    {
        private readonly DateTimeOffset _now;
        private int _calls;

        public StubRefreshClient(DateTimeOffset now)
        {
            _now = now;
            Result = TokenRefreshResult.Success(new TokenRefreshResponse(
                "agent-1",
                "device-1",
                "tenant-a",
                "Bearer",
                "rotated-access-token",
                "rotated-refresh-token",
                now.AddHours(1),
                now.AddDays(1)));
        }

        public int Calls => _calls;

        public TokenRefreshResult Result { get; set; }

        public async Task<TokenRefreshResult> RefreshAsync(
            Uri refreshEndpoint,
            TokenRefreshRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            await Task.Delay(20, cancellationToken);
            return Result;
        }
    }

    private sealed class TrackingCredentialStore : ICredentialStore
    {
        public int DeleteCalls { get; private set; }
        public int SaveCalls { get; private set; }
        public StoredEdgeCredentials? Stored { get; private set; }

        public Task<StoredEdgeCredentials?> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Stored);

        public Task SaveAsync(StoredEdgeCredentials credentials, CancellationToken cancellationToken)
        {
            SaveCalls++;
            Stored = credentials;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(CancellationToken cancellationToken)
        {
            DeleteCalls++;
            Stored = null;
            return Task.CompletedTask;
        }
    }
}
