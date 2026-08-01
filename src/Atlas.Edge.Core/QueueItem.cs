namespace Atlas.Edge.Core;

public sealed record QueueItem<T>(
    string ReceiptId,
    T Payload,
    int AttemptCount,
    DateTimeOffset EnqueuedAtUtc,
    DateTimeOffset? LastAttemptedAtUtc,
    DateTimeOffset? AvailableAfterUtc);