namespace Atlas.Edge.Core;

public sealed record AgentIdentity(
    string AgentId,
    string WorkstationId,
    string TenantBinding,
    string EnvironmentName,
    bool IsTemporaryDevelopmentIdentity,
    DateTimeOffset IssuedAtUtc);