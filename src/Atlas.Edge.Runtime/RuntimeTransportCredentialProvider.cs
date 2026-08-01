using Atlas.Edge.Enrollment;
using Atlas.Edge.Security;
using Atlas.Edge.Transport;

namespace Atlas.Edge.Runtime;

public sealed class RuntimeTransportCredentialProvider : ITransportCredentialProvider, IDisposable
{
    private readonly ICredentialStore _credentialStore;
    private readonly CredentialExpiryPolicy _expiryPolicy;
    private readonly ILogger<RuntimeTransportCredentialProvider> _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _sync = new();
    private readonly ITokenRefreshClient _tokenRefreshClient;
    private CredentialLease? _current;
    private CredentialLease? _pendingLease;
    private StoredEdgeCredentials? _pendingPersistence;
    private DateTimeOffset? _nextRefreshAttemptUtc;
    private int _refreshFailureCount;
    private CredentialLifecycleState _state = CredentialLifecycleState.Unenrolled;

    public RuntimeTransportCredentialProvider(
        ICredentialStore credentialStore,
        ITokenRefreshClient tokenRefreshClient,
        CredentialExpiryPolicy expiryPolicy,
        ILogger<RuntimeTransportCredentialProvider> logger)
    {
        _credentialStore = credentialStore;
        _tokenRefreshClient = tokenRefreshClient;
        _expiryPolicy = expiryPolicy;
        _logger = logger;
    }

    public CredentialLifecycleState State
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    public void Initialize(StoredEdgeCredentials credentials)
    {
        var lease = ToLease(credentials);

        lock (_sync)
        {
            _current = lease;
            _pendingLease = null;
            _pendingPersistence = null;
            _nextRefreshAttemptUtc = null;
            _refreshFailureCount = 0;
            _state = CredentialLifecycleState.Active;
        }
    }

    public ValueTask<CredentialLeaseResult> GetLeaseAsync(CancellationToken cancellationToken) =>
        RefreshCoreAsync(expectedGeneration: null, forceRefresh: false, cancellationToken);

    public ValueTask<CredentialLeaseResult> RefreshAfterAccessTokenExpiredAsync(
        long rejectedGeneration,
        CancellationToken cancellationToken) =>
        RefreshCoreAsync(rejectedGeneration, forceRefresh: true, cancellationToken);

    public async ValueTask<CredentialLeaseResult> InvalidateAfterAuthenticationFailureAsync(
        long rejectedGeneration,
        string errorCode,
        CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = GetSnapshot();
            if (snapshot.Lease is not null && snapshot.Lease.Generation != rejectedGeneration)
            {
                return CredentialLeaseResult.Available(snapshot.Lease);
            }

            return await EnterAuthenticationRequiredAsync(errorCode, cancellationToken);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public void Dispose()
    {
        _refreshGate.Dispose();
    }

    private async ValueTask<CredentialLeaseResult> RefreshCoreAsync(
        long? expectedGeneration,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var snapshot = GetSnapshot();
        if (snapshot.State == CredentialLifecycleState.AuthenticationRequired)
        {
            return CredentialLeaseResult.Unavailable(CredentialAvailabilityKind.AuthenticationRequired, "authentication_required");
        }

        if (snapshot.Lease is null)
        {
            return CredentialLeaseResult.Unavailable(CredentialAvailabilityKind.Unenrolled, "unenrolled");
        }

        if (expectedGeneration.HasValue && snapshot.Lease.Generation != expectedGeneration.Value)
        {
            return CredentialLeaseResult.Available(snapshot.Lease);
        }

        if (!forceRefresh && !_expiryPolicy.IsRefreshDue(snapshot.Lease))
        {
            return CredentialLeaseResult.Available(snapshot.Lease);
        }

        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            snapshot = GetSnapshot();
            if (snapshot.State == CredentialLifecycleState.AuthenticationRequired)
            {
                return CredentialLeaseResult.Unavailable(CredentialAvailabilityKind.AuthenticationRequired, "authentication_required");
            }

            if (snapshot.Lease is null)
            {
                return CredentialLeaseResult.Unavailable(CredentialAvailabilityKind.Unenrolled, "unenrolled");
            }

            if (expectedGeneration.HasValue && snapshot.Lease.Generation != expectedGeneration.Value)
            {
                return CredentialLeaseResult.Available(snapshot.Lease);
            }

            var pendingResult = await TryPersistPendingAsync(snapshot.Lease, cancellationToken);
            if (pendingResult is not null)
            {
                return pendingResult;
            }

            snapshot = GetSnapshot();
            if (snapshot.Lease is null)
            {
                return CredentialLeaseResult.Unavailable(CredentialAvailabilityKind.Unenrolled, "unenrolled");
            }

            if (!forceRefresh && !_expiryPolicy.IsRefreshDue(snapshot.Lease))
            {
                return CredentialLeaseResult.Available(snapshot.Lease);
            }

            if (snapshot.NextRefreshAttemptUtc > _expiryPolicy.UtcNow)
            {
                return _expiryPolicy.CanUseAccessToken(snapshot.Lease)
                    ? CredentialLeaseResult.Available(snapshot.Lease)
                    : CredentialLeaseResult.Unavailable(CredentialAvailabilityKind.RetryableUnavailable, "refresh_backoff");
            }

            if (!_expiryPolicy.CanUseRefreshToken(snapshot.Lease))
            {
                return await EnterAuthenticationRequiredAsync("refresh_token_expired", cancellationToken);
            }

            if (!Uri.TryCreate(snapshot.Lease.RefreshUrl, UriKind.Absolute, out var refreshEndpoint))
            {
                return await EnterAuthenticationRequiredAsync("invalid_refresh_endpoint", cancellationToken);
            }

            SetState(CredentialLifecycleState.Refreshing);

            var request = new TokenRefreshRequest(
                snapshot.Lease.AgentId,
                snapshot.Lease.DeviceId,
                snapshot.Lease.TenantBinding,
                snapshot.Lease.RefreshToken,
                _expiryPolicy.UtcNow);
            var refreshResult = await _tokenRefreshClient.RefreshAsync(refreshEndpoint, request, cancellationToken);

            if (refreshResult.Response is not null)
            {
                return await PersistAndPublishAsync(snapshot.Lease, refreshResult.Response, cancellationToken);
            }

            if (refreshResult.IsPermanent)
            {
                return await EnterAuthenticationRequiredAsync(refreshResult.ErrorCode ?? "refresh_rejected", cancellationToken);
            }

            return HandleTransientFailure(snapshot.Lease, refreshResult.ErrorCode ?? "refresh_transient_failure");
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task<CredentialLeaseResult?> TryPersistPendingAsync(
        CredentialLease currentLease,
        CancellationToken cancellationToken)
    {
        StoredEdgeCredentials? pendingCredentials;
        CredentialLease? pendingLease;
        lock (_sync)
        {
            pendingCredentials = _pendingPersistence;
            pendingLease = _pendingLease;
        }

        if (pendingCredentials is null || pendingLease is null)
        {
            return null;
        }

        try
        {
            await _credentialStore.SaveAsync(pendingCredentials, cancellationToken);
            Publish(pendingLease);
            return CredentialLeaseResult.Available(pendingLease);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return HandlePersistenceFailure(currentLease);
        }
    }

    private async Task<CredentialLeaseResult> PersistAndPublishAsync(
        CredentialLease previousLease,
        TokenRefreshResponse response,
        CancellationToken cancellationToken)
    {
        var generation = checked(previousLease.Generation + 1);
        var credentials = new StoredEdgeCredentials(
            new Atlas.Edge.Core.AgentIdentity(
                response.AgentId,
                response.DeviceId,
                response.TenantBinding,
                previousLease.EnvironmentName,
                false,
                previousLease.IssuedAtUtc),
            response.DeviceId,
            previousLease.IngestionUrl,
            previousLease.SiteTimezone,
            response.AccessToken,
            response.RefreshToken,
            response.AccessTokenExpiryUtc.ToUniversalTime(),
            response.RefreshTokenExpiryUtc.ToUniversalTime(),
            previousLease.RefreshUrl,
            generation,
            _expiryPolicy.UtcNow);
        var lease = ToLease(credentials);

        try
        {
            await _credentialStore.SaveAsync(credentials, cancellationToken);
            Publish(lease);
            _logger.LogInformation(
                "Credential refresh succeeded for agent {AgentId}; generation {Generation}; access token fingerprint {TokenFingerprint}.",
                lease.AgentId,
                lease.Generation,
                SecretRedactor.Redact(lease.AccessToken));
            return CredentialLeaseResult.Available(lease);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lock (_sync)
            {
                _pendingPersistence = credentials;
                _pendingLease = lease;
            }

            return HandlePersistenceFailure(previousLease);
        }
    }

    private CredentialLeaseResult HandlePersistenceFailure(CredentialLease previousLease)
    {
        ScheduleRetry();
        SetState(_expiryPolicy.CanUseAccessToken(previousLease)
            ? CredentialLifecycleState.Active
            : CredentialLifecycleState.Refreshing);
        _logger.LogWarning("Credential refresh persistence failed; protected persistence will be retried.");
        return _expiryPolicy.CanUseAccessToken(previousLease)
            ? CredentialLeaseResult.Available(previousLease)
            : CredentialLeaseResult.Unavailable(CredentialAvailabilityKind.RetryableUnavailable, "credential_persistence_failed");
    }

    private CredentialLeaseResult HandleTransientFailure(CredentialLease lease, string errorCode)
    {
        ScheduleRetry();
        SetState(_expiryPolicy.CanUseAccessToken(lease)
            ? CredentialLifecycleState.Active
            : CredentialLifecycleState.Refreshing);
        _logger.LogWarning(
            "Credential refresh failed transiently with code {ErrorCode}; retry scheduled for {NextRetryUtc}.",
            errorCode,
            GetSnapshot().NextRefreshAttemptUtc);
        return _expiryPolicy.CanUseAccessToken(lease)
            ? CredentialLeaseResult.Available(lease)
            : CredentialLeaseResult.Unavailable(CredentialAvailabilityKind.RetryableUnavailable, errorCode);
    }

    private async Task<CredentialLeaseResult> EnterAuthenticationRequiredAsync(
        string errorCode,
        CancellationToken cancellationToken)
    {
        try
        {
            await _credentialStore.DeleteAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Failed to remove unusable protected credentials; authentication remains required.");
        }

        lock (_sync)
        {
            _current = null;
            _pendingLease = null;
            _pendingPersistence = null;
            _state = CredentialLifecycleState.AuthenticationRequired;
        }

        _logger.LogError("Credential refresh permanently failed with code {ErrorCode}; authentication is required.", errorCode);
        return CredentialLeaseResult.Unavailable(CredentialAvailabilityKind.AuthenticationRequired, errorCode);
    }

    private void Publish(CredentialLease lease)
    {
        lock (_sync)
        {
            _current = lease;
            _pendingLease = null;
            _pendingPersistence = null;
            _nextRefreshAttemptUtc = null;
            _refreshFailureCount = 0;
            _state = CredentialLifecycleState.Active;
        }
    }

    private void ScheduleRetry()
    {
        lock (_sync)
        {
            _refreshFailureCount++;
            _nextRefreshAttemptUtc = _expiryPolicy.UtcNow + _expiryPolicy.GetRetryDelay(_refreshFailureCount);
        }
    }

    private void SetState(CredentialLifecycleState state)
    {
        lock (_sync)
        {
            _state = state;
        }
    }

    private Snapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new Snapshot(_current, _state, _nextRefreshAttemptUtc);
        }
    }

    private static CredentialLease ToLease(StoredEdgeCredentials credentials) =>
        new(
            credentials.Identity.AgentId,
            credentials.DeviceId,
            credentials.Identity.TenantBinding,
            credentials.IngestionUrl,
            credentials.SiteTimezone,
            credentials.AccessToken,
            credentials.RefreshToken,
            credentials.AccessTokenExpiryUtc.ToUniversalTime(),
            credentials.RefreshTokenExpiryUtc.ToUniversalTime(),
            credentials.RefreshUrl,
            Math.Max(1, credentials.Generation),
            credentials.Identity.EnvironmentName,
            credentials.Identity.IssuedAtUtc);

    private sealed record Snapshot(
        CredentialLease? Lease,
        CredentialLifecycleState State,
        DateTimeOffset? NextRefreshAttemptUtc);
}
