using Atlas.Edge.Configuration;
using Atlas.Edge.Core;
using Atlas.Edge.Enrollment;
using Atlas.Edge.Queue;
using Atlas.Edge.Security;
using Atlas.Edge.Telemetry;
using Atlas.Edge.Transport;
using Microsoft.Extensions.Options;

namespace Atlas.Edge.Runtime;

public sealed class Worker : BackgroundService
{
    private readonly DevelopmentIdentityProvider _identityProvider;
    private readonly IEnrollmentClient _enrollmentClient;
    private readonly HeartbeatEventBuilder _heartbeatEventBuilder;
    private readonly ICredentialStore _credentialStore;
    private readonly ILogger<Worker> _logger;
    private readonly AtlasEdgeOptions _options;
    private readonly IEventQueue _queue;
    private readonly RuntimeState _runtimeState;
    private readonly RuntimeIdentityState _runtimeIdentityState;
    private readonly RuntimeTransportCredentialProvider _transportCredentialProvider;
    private readonly IEventTransport _transport;
    private readonly TimeProvider _timeProvider;
    private AgentIdentity? _identity;

    public Worker(
        IOptions<AtlasEdgeOptions> options,
        DevelopmentIdentityProvider identityProvider,
        IEnrollmentClient enrollmentClient,
        HeartbeatEventBuilder heartbeatEventBuilder,
        ICredentialStore credentialStore,
        IEventQueue queue,
        IEventTransport transport,
        RuntimeTransportCredentialProvider transportCredentialProvider,
        RuntimeState runtimeState,
        RuntimeIdentityState runtimeIdentityState,
        TimeProvider timeProvider,
        ILogger<Worker> logger)
    {
        _identityProvider = identityProvider;
        _enrollmentClient = enrollmentClient;
        _heartbeatEventBuilder = heartbeatEventBuilder;
        _credentialStore = credentialStore;
        _queue = queue;
        _transport = transport;
        _transportCredentialProvider = transportCredentialProvider;
        _runtimeState = runtimeState;
        _runtimeIdentityState = runtimeIdentityState;
        _timeProvider = timeProvider;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _runtimeState.Update(RuntimeStatus.Starting, "Runtime starting.");
        _logger.LogInformation(
            "Atlas Edge runtime starting for environment {EnvironmentName}.",
            _options.EnvironmentName);

        _runtimeState.Update(RuntimeStatus.Running, "Runtime running.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var identity = await EnsureIdentityAsync(stoppingToken);
                if (identity is null)
                {
                    _runtimeState.Update(RuntimeStatus.Degraded, "Identity is unavailable. Enrollment will be retried.");
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(_options.HeartbeatIntervalSeconds, 5)), _timeProvider, stoppingToken);
                    continue;
                }

                await ProcessHeartbeatAsync(identity, stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(_options.HeartbeatIntervalSeconds), _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _runtimeState.Update(RuntimeStatus.Degraded, "Heartbeat cycle failed.", ex.Message);
                _logger.LogError(ex, "Heartbeat cycle failed and will be retried without stopping the runtime.");

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(_options.HeartbeatIntervalSeconds, 5)), _timeProvider, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _runtimeState.Update(RuntimeStatus.Stopping, "Runtime stopping.");
        _logger.LogInformation("Atlas Edge runtime stopping gracefully.");
        return base.StopAsync(cancellationToken);
    }

    private async Task ProcessHeartbeatAsync(AgentIdentity identity, CancellationToken cancellationToken)
    {
        var queueBeforeHeartbeat = await _queue.GetHealthAsync(cancellationToken);
        var heartbeat = _heartbeatEventBuilder.Build(
            identity,
            _options,
            _timeProvider.GetUtcNow(),
            queueBeforeHeartbeat);
        var receiptId = await _queue.EnqueueAsync(heartbeat, cancellationToken);

        _logger.LogInformation(
            "Generated heartbeat event {EventId} and queued it as receipt {ReceiptId}.",
            heartbeat.EventId,
            receiptId);

        var batch = await _queue.PeekBatchAsync(_options.QueueBatchSize, cancellationToken);
        if (batch.Count == 0)
        {
            return;
        }

        var result = await _transport.SendAsync(batch, cancellationToken);
        if (result.IsSuccess)
        {
            var acceptedReceipts = batch
                .Where(item => result.AcceptedEventIds.Contains(item.Payload.EventId))
                .Select(item => item.ReceiptId)
                .ToArray();

            await _queue.AcknowledgeAsync(acceptedReceipts, cancellationToken);

            var rejectedReceipts = batch
                .Where(item => !result.AcceptedEventIds.Contains(item.Payload.EventId))
                .Select(item => item.ReceiptId)
                .ToArray();

            if (rejectedReceipts.Length > 0)
            {
                await RetryAsync(batch, rejectedReceipts, cancellationToken);
            }
        }
        else if (result.FailureKind == TransportFailureKind.Retryable)
        {
            await RetryAsync(batch, batch.Select(item => item.ReceiptId), cancellationToken);

            _runtimeState.Update(RuntimeStatus.Degraded, "Heartbeat delivery failed and is retryable.", result.Error);
            _logger.LogWarning("Retryable transport failure occurred: {Failure}", result.Error);
        }
        else if (result.FailureKind == TransportFailureKind.AuthenticationRequired)
        {
            await RetryAsync(batch, batch.Select(item => item.ReceiptId), cancellationToken);

            _runtimeState.Update(RuntimeStatus.Degraded, "Authentication is required; telemetry remains queued.", result.Error);
            _logger.LogWarning("Authenticated transmission is paused with code {Failure}; telemetry remains queued.", result.Error);
        }
        else
        {
            await _queue.AcknowledgeAsync(batch.Select(item => item.ReceiptId), cancellationToken);

            _runtimeState.Update(RuntimeStatus.Degraded, "Heartbeat delivery failed and is non-retryable.", result.Error);
            _logger.LogWarning("Non-retryable transport failure occurred. Dropping batch safely: {Failure}", result.Error);
        }

        var queueHealth = await _queue.GetHealthAsync(cancellationToken);
        if (result.IsSuccess)
        {
            _runtimeState.Update(RuntimeStatus.Running, "Heartbeat cycle completed.");
        }

        _logger.LogInformation(
            "Heartbeat cycle completed with queue pending count {PendingCount} and in-flight count {InFlightCount}.",
            queueHealth.PendingCount,
            queueHealth.InFlightCount);
    }

    private Task RetryAsync(
        IReadOnlyList<QueueItem<AgentHeartbeatEvent>> batch,
        IEnumerable<string> receiptIds,
        CancellationToken cancellationToken)
    {
        var attempt = Math.Max(1, batch.Count == 0 ? 1 : batch.Max(item => item.AttemptCount));
        return _queue.RetryAsync(
            receiptIds,
            _timeProvider.GetUtcNow() + GetQueueRetryDelay(
                attempt,
                _options.QueueRetryBaseSeconds,
                _options.QueueRetryMaximumSeconds),
            cancellationToken);
    }

    internal static TimeSpan GetQueueRetryDelay(int attempt, int baseSeconds, int maximumSeconds)
    {
        var exponent = Math.Min(Math.Max(1, attempt) - 1, 20);
        var delaySeconds = Math.Min(maximumSeconds, baseSeconds * Math.Pow(2, exponent));
        return TimeSpan.FromSeconds(delaySeconds);
    }

    private async Task<AgentIdentity?> EnsureIdentityAsync(CancellationToken cancellationToken)
    {
        if (_identity is not null)
        {
            _runtimeIdentityState.Update(_identity);
            return _identity;
        }

        if (string.Equals(_options.TransportMode, AtlasEdgeOptions.TransportModeNull, StringComparison.OrdinalIgnoreCase))
        {
            _identity = _identityProvider.Create(_options);
            _runtimeIdentityState.Update(_identity);
            _logger.LogInformation("Using null transport development identity for agent {AgentId}.", _identity.AgentId);
            return _identity;
        }

        var storedCredentials = await _credentialStore.LoadAsync(cancellationToken);
        if (storedCredentials is not null)
        {
            _identity = storedCredentials.Identity;
            _runtimeIdentityState.Update(_identity);
            _transportCredentialProvider.Initialize(storedCredentials);

            _logger.LogInformation(
                "Loaded stored device identity for agent {AgentId} with token fingerprint {TokenFingerprint}.",
                storedCredentials.Identity.AgentId,
                SecretRedactor.Redact(storedCredentials.AccessToken));

            return _identity;
        }

        if (string.IsNullOrWhiteSpace(_options.EnrollmentCode))
        {
            _logger.LogWarning("Enrollment code is not configured. Enrollment cannot be completed yet.");
            return null;
        }

        var enrollmentRequest = new EnrollmentRequest(
            _options.EnrollmentCode,
            _options.EnvironmentName,
            Environment.MachineName,
            _timeProvider.GetUtcNow());

        var enrollmentResult = await _enrollmentClient.EnrollAsync(enrollmentRequest, cancellationToken);
        if (enrollmentResult.Response is null)
        {
            if (enrollmentResult.IsRetryable)
            {
                _logger.LogWarning("Enrollment failed with a retryable error: {Error}", enrollmentResult.Error);
            }
            else
            {
                _logger.LogError("Enrollment failed with a non-retryable error: {Error}", enrollmentResult.Error);
            }

            return null;
        }

        var response = enrollmentResult.Response;
        _identity = new AgentIdentity(
            response.AgentId,
            response.DeviceId,
            response.TenantBinding,
            _options.EnvironmentName,
            false,
            _timeProvider.GetUtcNow());
        _runtimeIdentityState.Update(_identity);

        var credentials = new StoredEdgeCredentials(
            _identity,
            response.DeviceId,
            response.IngestionUrl,
            response.SiteTimezone,
            response.AccessToken,
            response.RefreshToken,
            response.CredentialExpiryUtc.ToUniversalTime(),
            response.RefreshTokenExpiryUtc.ToUniversalTime(),
            response.TokenRefreshUrl,
            1,
            _timeProvider.GetUtcNow());

        await _credentialStore.SaveAsync(credentials, cancellationToken);

        _transportCredentialProvider.Initialize(credentials);

        _logger.LogInformation(
            "Enrollment succeeded for agent {AgentId}; site timezone {SiteTimezone}; token fingerprint {TokenFingerprint}.",
            response.AgentId,
            response.SiteTimezone,
            SecretRedactor.Redact(response.AccessToken));

        return _identity;
    }
}
