using Atlas.Edge.Security;

namespace Atlas.Edge.Transport;

public enum CredentialAvailabilityKind
{
    Available = 0,
    RetryableUnavailable = 1,
    AuthenticationRequired = 2,
    Unenrolled = 3
}

public sealed record CredentialLeaseResult(
    CredentialLease? Lease,
    CredentialAvailabilityKind Kind,
    string? ErrorCode)
{
    public static CredentialLeaseResult Available(CredentialLease lease) =>
        new(lease, CredentialAvailabilityKind.Available, null);

    public static CredentialLeaseResult Unavailable(CredentialAvailabilityKind kind, string errorCode) =>
        new(null, kind, errorCode);
}

public interface ITransportCredentialProvider
{
    CredentialLifecycleState State { get; }

    ValueTask<CredentialLeaseResult> GetLeaseAsync(CancellationToken cancellationToken);

    ValueTask<CredentialLeaseResult> RefreshAfterAccessTokenExpiredAsync(
        long rejectedGeneration,
        CancellationToken cancellationToken);

    ValueTask<CredentialLeaseResult> InvalidateAfterAuthenticationFailureAsync(
        long rejectedGeneration,
        string errorCode,
        CancellationToken cancellationToken);
}
