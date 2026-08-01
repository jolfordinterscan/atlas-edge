using Atlas.Edge.Core;

namespace Atlas.Edge.Security;

public sealed record StoredEdgeCredentials(
    AgentIdentity Identity,
    string DeviceId,
    string IngestionUrl,
    string SiteTimezone,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset CredentialExpiryUtc,
    DateTimeOffset StoredAtUtc);
