using System.ComponentModel.DataAnnotations;

namespace Atlas.Edge.Configuration;

public sealed class AtlasEdgeOptions
{
    public const string SectionName = "AtlasEdge";

    [Required]
    public string AgentId { get; set; } = "dev-agent-placeholder";

    [Required]
    public string WorkstationId { get; set; } = "dev-workstation-placeholder";

    [Required]
    public string TenantBinding { get; set; } = "tenant-placeholder";

    [Required]
    public string IngestionUrl { get; set; } = "https://example.invalid/atlas-ingestion-placeholder";

    [Range(1, 86400)]
    public int HeartbeatIntervalSeconds { get; set; } = 60;

    [Range(1, 1000)]
    public int QueueBatchSize { get; set; } = 10;

    [Required]
    public string EnvironmentName { get; set; } = "Development";
}