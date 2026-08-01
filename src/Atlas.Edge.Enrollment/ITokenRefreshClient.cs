namespace Atlas.Edge.Enrollment;

public interface ITokenRefreshClient
{
    Task<TokenRefreshResult> RefreshAsync(
        Uri refreshEndpoint,
        TokenRefreshRequest request,
        CancellationToken cancellationToken);
}
