namespace Atlas.Edge.Core;

public sealed record RuntimeHealthState(
    RuntimeStatus Status,
    DateTimeOffset LastUpdatedUtc,
    string Message,
    string? LastError);