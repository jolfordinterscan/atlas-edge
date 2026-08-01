namespace Atlas.Edge.Core;

public sealed record QueueHealth(int PendingCount, int InFlightCount, DateTimeOffset ObservedAtUtc);