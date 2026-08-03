using System.Text.Json;
using Atlas.Edge.Core;

namespace Atlas.Edge.Queue;

public sealed class JsonFileEventQueue : IEventQueue
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly int _maximumPendingEvents;
    private readonly string _path;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _retention;
    private QueueState? _state;

    public JsonFileEventQueue(
        string path,
        int maximumPendingEvents,
        TimeSpan retention,
        TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumPendingEvents, 1);
        if (retention <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retention));
        }

        _path = Path.GetFullPath(path);
        _maximumPendingEvents = maximumPendingEvents;
        _retention = retention;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<string> EnqueueAsync(
        AgentHeartbeatEvent heartbeatEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(heartbeatEvent);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await StateAsync(cancellationToken).ConfigureAwait(false);
            PruneExpired(state);
            while (state.Heartbeats.Count >= _maximumPendingEvents)
            {
                var oldest = state.Heartbeats
                    .Where(entry => !entry.InFlight)
                    .OrderBy(entry => entry.EnqueuedAtUtc)
                    .FirstOrDefault();
                if (oldest is null)
                {
                    throw new InvalidOperationException("The durable event queue is at capacity.");
                }

                state.Heartbeats.Remove(oldest);
            }

            var receiptId = Guid.NewGuid().ToString("N");
            var now = _timeProvider.GetUtcNow();
            state.Heartbeats.Add(new HeartbeatEntry(
                receiptId,
                heartbeatEvent,
                0,
                now,
                null,
                now,
                false));
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return receiptId;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ScannerInventoryEnqueueResult> EnqueueInventoryAsync(
        ScannerInventoryEvent inventoryEvent,
        CancellationToken cancellationToken) =>
        await EnqueueInventoryAsync(inventoryEvent, false, cancellationToken).ConfigureAwait(false);

    public async Task<ScannerInventoryEnqueueResult> EnqueueInventoryAsync(
        ScannerInventoryEvent inventoryEvent,
        bool forceReconciliation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inventoryEvent);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await StateAsync(cancellationToken).ConfigureAwait(false);
            if (string.Equals(state.PendingInventory?.Event.InventoryVersion, inventoryEvent.InventoryVersion, StringComparison.Ordinal) ||
                (!forceReconciliation &&
                 string.Equals(state.LastAcknowledgedInventoryVersion, inventoryEvent.InventoryVersion, StringComparison.Ordinal)))
            {
                return new ScannerInventoryEnqueueResult(state.PendingInventory?.ReceiptId ?? string.Empty, false);
            }

            var receiptId = Guid.NewGuid().ToString("N");
            state.PendingInventory = new InventoryEntry(receiptId, inventoryEvent);
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return new ScannerInventoryEnqueueResult(receiptId, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ScannerInventoryEvent?> GetLatestInventoryAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return (await StateAsync(cancellationToken).ConfigureAwait(false)).PendingInventory?.Event;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AcknowledgeInventoryAsync(string eventId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await StateAsync(cancellationToken).ConfigureAwait(false);
            var pending = state.PendingInventory;
            if (!string.Equals(pending?.Event.EventId, eventId, StringComparison.Ordinal))
            {
                return;
            }

            state.LastAcknowledgedInventoryVersion = pending!.Event.InventoryVersion;
            state.PendingInventory = null;
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<QueueItem<AgentHeartbeatEvent>>> PeekBatchAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await StateAsync(cancellationToken).ConfigureAwait(false);
            PruneExpired(state);
            var now = _timeProvider.GetUtcNow();
            var selected = state.Heartbeats
                .Where(entry => !entry.InFlight && entry.AvailableAfterUtc <= now)
                .OrderBy(entry => entry.EnqueuedAtUtc)
                .Take(batchSize)
                .ToArray();
            foreach (var entry in selected)
            {
                var index = state.Heartbeats.IndexOf(entry);
                state.Heartbeats[index] = entry with
                {
                    AttemptCount = entry.AttemptCount + 1,
                    LastAttemptedAtUtc = now,
                    InFlight = true
                };
            }

            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return selected.Select(entry =>
            {
                var current = state.Heartbeats[state.Heartbeats.FindIndex(value => value.ReceiptId == entry.ReceiptId)];
                return current.ToQueueItem();
            }).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AcknowledgeAsync(IEnumerable<string> receiptIds, CancellationToken cancellationToken)
    {
        var ids = receiptIds.ToHashSet(StringComparer.Ordinal);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await StateAsync(cancellationToken).ConfigureAwait(false);
            state.Heartbeats.RemoveAll(entry => ids.Contains(entry.ReceiptId));
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RetryAsync(
        IEnumerable<string> receiptIds,
        DateTimeOffset availableAfterUtc,
        CancellationToken cancellationToken)
    {
        var ids = receiptIds.ToHashSet(StringComparer.Ordinal);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await StateAsync(cancellationToken).ConfigureAwait(false);
            for (var index = 0; index < state.Heartbeats.Count; index++)
            {
                var entry = state.Heartbeats[index];
                if (ids.Contains(entry.ReceiptId))
                {
                    state.Heartbeats[index] = entry with
                    {
                        InFlight = false,
                        AvailableAfterUtc = availableAfterUtc.ToUniversalTime()
                    };
                }
            }

            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<QueueHealth> GetHealthAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await StateAsync(cancellationToken).ConfigureAwait(false);
            PruneExpired(state);
            var pending = state.Heartbeats.Count(entry => !entry.InFlight) +
                (state.PendingInventory is null ? 0 : 1);
            var inFlight = state.Heartbeats.Count(entry => entry.InFlight);
            return new QueueHealth(pending, inFlight, _timeProvider.GetUtcNow());
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<QueueState> StateAsync(CancellationToken cancellationToken)
    {
        if (_state is not null)
        {
            return _state;
        }

        if (!File.Exists(_path))
        {
            _state = new QueueState();
            return _state;
        }

        await using var stream = File.OpenRead(_path);
        _state = await JsonSerializer.DeserializeAsync<QueueState>(stream, SerializerOptions, cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidDataException("The durable event queue is empty or invalid.");
        _state.Heartbeats = _state.Heartbeats
            .Select(entry => entry with { InFlight = false })
            .ToList();
        return _state;
    }

    private async Task SaveAsync(QueueState state, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path) ?? throw new InvalidOperationException("Queue path has no directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = _path + ".tmp";
        await using (var stream = new FileStream(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.WriteThrough | FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(stream, state, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporaryPath, _path, true);
    }

    private void PruneExpired(QueueState state)
    {
        var cutoff = _timeProvider.GetUtcNow() - _retention;
        state.Heartbeats.RemoveAll(entry => !entry.InFlight && entry.EnqueuedAtUtc < cutoff);
    }

    private sealed record HeartbeatEntry(
        string ReceiptId,
        AgentHeartbeatEvent Payload,
        int AttemptCount,
        DateTimeOffset EnqueuedAtUtc,
        DateTimeOffset? LastAttemptedAtUtc,
        DateTimeOffset AvailableAfterUtc,
        bool InFlight)
    {
        public QueueItem<AgentHeartbeatEvent> ToQueueItem() => new(
            ReceiptId,
            Payload,
            AttemptCount,
            EnqueuedAtUtc,
            LastAttemptedAtUtc,
            AvailableAfterUtc);
    }

    private sealed record InventoryEntry(string ReceiptId, ScannerInventoryEvent Event);

    private sealed class QueueState
    {
        public List<HeartbeatEntry> Heartbeats { get; set; } = [];

        public InventoryEntry? PendingInventory { get; set; }

        public string? LastAcknowledgedInventoryVersion { get; set; }
    }
}
