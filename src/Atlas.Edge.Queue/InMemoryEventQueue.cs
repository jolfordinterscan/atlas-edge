using System.Collections.Concurrent;
using Atlas.Edge.Core;

namespace Atlas.Edge.Queue;

public sealed class InMemoryEventQueue : IEventQueue
{
    private readonly ConcurrentDictionary<string, QueueEntry> _entries = new();

    public Task<string> EnqueueAsync(AgentHeartbeatEvent heartbeatEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var receiptId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;

        _entries[receiptId] = new QueueEntry(
            receiptId,
            heartbeatEvent,
            0,
            now,
            null,
            now,
            false);

        return Task.FromResult(receiptId);
    }

    public Task<IReadOnlyList<QueueItem<AgentHeartbeatEvent>>> PeekBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = DateTimeOffset.UtcNow;
        var selected = new List<QueueItem<AgentHeartbeatEvent>>();

        foreach (var pair in _entries.OrderBy(entry => entry.Value.EnqueuedAtUtc))
        {
            if (selected.Count >= batchSize)
            {
                break;
            }

            var current = pair.Value;
            if (current.InFlight || current.AvailableAfterUtc > now)
            {
                continue;
            }

            var updated = current with
            {
                InFlight = true,
                AttemptCount = current.AttemptCount + 1,
                LastAttemptedAtUtc = now
            };

            if (_entries.TryUpdate(pair.Key, updated, current))
            {
                selected.Add(updated.ToQueueItem());
            }
        }

        return Task.FromResult<IReadOnlyList<QueueItem<AgentHeartbeatEvent>>>(selected);
    }

    public Task AcknowledgeAsync(IEnumerable<string> receiptIds, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var receiptId in receiptIds)
        {
            _entries.TryRemove(receiptId, out _);
        }

        return Task.CompletedTask;
    }

    public Task RetryAsync(IEnumerable<string> receiptIds, DateTimeOffset availableAfterUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var receiptId in receiptIds)
        {
            if (_entries.TryGetValue(receiptId, out var entry))
            {
                var updated = entry with
                {
                    InFlight = false,
                    AvailableAfterUtc = availableAfterUtc.ToUniversalTime()
                };

                _entries.TryUpdate(receiptId, updated, entry);
            }
        }

        return Task.CompletedTask;
    }

    public Task<QueueHealth> GetHealthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var pendingCount = _entries.Values.Count(entry => !entry.InFlight);
        var inFlightCount = _entries.Values.Count(entry => entry.InFlight);

        return Task.FromResult(new QueueHealth(pendingCount, inFlightCount, DateTimeOffset.UtcNow));
    }

    private sealed record QueueEntry(
        string ReceiptId,
        AgentHeartbeatEvent Payload,
        int AttemptCount,
        DateTimeOffset EnqueuedAtUtc,
        DateTimeOffset? LastAttemptedAtUtc,
        DateTimeOffset AvailableAfterUtc,
        bool InFlight)
    {
        public QueueItem<AgentHeartbeatEvent> ToQueueItem() =>
            new(
                ReceiptId,
                Payload,
                AttemptCount,
                EnqueuedAtUtc,
                LastAttemptedAtUtc,
                AvailableAfterUtc);
    }
}