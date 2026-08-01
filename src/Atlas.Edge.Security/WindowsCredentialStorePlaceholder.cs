namespace Atlas.Edge.Security;

public sealed class WindowsCredentialStorePlaceholder : ICredentialStore
{
    public Task<StoredEdgeCredentials?> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new PlatformNotSupportedException("Windows protected credential storage is not implemented yet.");
    }

    public Task SaveAsync(StoredEdgeCredentials credentials, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new PlatformNotSupportedException("Windows protected credential storage is not implemented yet.");
    }

    public Task DeleteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new PlatformNotSupportedException("Windows protected credential storage is not implemented yet.");
    }
}
