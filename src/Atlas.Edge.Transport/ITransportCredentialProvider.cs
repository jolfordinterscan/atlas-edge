namespace Atlas.Edge.Transport;

public sealed record TransportCredentialContext(
    string IngestionUrl,
    string AgentId,
    string TenantBinding,
    string AccessToken);

public interface ITransportCredentialProvider
{
    TransportCredentialContext? GetCurrent();
}
