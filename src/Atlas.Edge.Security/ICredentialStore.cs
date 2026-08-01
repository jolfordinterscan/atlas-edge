namespace Atlas.Edge.Security;

public interface ICredentialStore
{
    Task<StoredEdgeCredentials?> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(StoredEdgeCredentials credentials, CancellationToken cancellationToken);
}
