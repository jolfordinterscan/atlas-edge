using System.ComponentModel.DataAnnotations;

namespace Atlas.Edge.Configuration;

public sealed class AtlasEdgeOptions
{
    public const string SectionName = "AtlasEdge";
    public const string TransportModeNull = "Null";
    public const string TransportModeHttp = "Http";

    public string AgentId { get; set; } = "dev-agent-placeholder";

    public string WorkstationId { get; set; } = "dev-workstation-placeholder";

    public string TenantBinding { get; set; } = "tenant-placeholder";

    public string IngestionUrl { get; set; } = "https://example.invalid/atlas-ingestion-placeholder";

    [Required]
    public string EnrollmentUrl { get; set; } = "https://localhost:7143/";

    public string EnrollmentCode { get; set; } = string.Empty;

    [Range(1, 300)]
    public int HttpTimeoutSeconds { get; set; } = 15;

    public bool AllowInsecureHttpForDevelopment { get; set; }

    [Required]
    public string TransportMode { get; set; } = TransportModeNull;

    public string? CredentialStorePath { get; set; }

    [Required]
    public string SiteTimezone { get; set; } = "UTC";

    [Range(1, 86400)]
    public int HeartbeatIntervalSeconds { get; set; } = 60;

    [Range(1, 1000)]
    public int QueueBatchSize { get; set; } = 10;

    [Required]
    public string EnvironmentName { get; set; } = "Development";
}
