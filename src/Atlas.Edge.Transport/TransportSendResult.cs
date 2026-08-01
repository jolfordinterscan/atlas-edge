namespace Atlas.Edge.Transport;

public enum TransportFailureKind
{
    None = 0,
    Retryable = 1,
    NonRetryable = 2
}

public sealed record TransportSendResult(
    IReadOnlySet<string> AcceptedEventIds,
    TransportFailureKind FailureKind,
    string? Error)
{
    public bool IsSuccess => FailureKind == TransportFailureKind.None;

    public static TransportSendResult Success(IEnumerable<string> acceptedEventIds) =>
        new(acceptedEventIds.ToHashSet(StringComparer.Ordinal), TransportFailureKind.None, null);

    public static TransportSendResult Retryable(string error, IEnumerable<string>? acceptedEventIds = null) =>
        new((acceptedEventIds ?? Array.Empty<string>()).ToHashSet(StringComparer.Ordinal), TransportFailureKind.Retryable, error);

    public static TransportSendResult NonRetryable(string error, IEnumerable<string>? acceptedEventIds = null) =>
        new((acceptedEventIds ?? Array.Empty<string>()).ToHashSet(StringComparer.Ordinal), TransportFailureKind.NonRetryable, error);
}
