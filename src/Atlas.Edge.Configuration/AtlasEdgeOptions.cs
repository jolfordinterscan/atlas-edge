using System.ComponentModel.DataAnnotations;

namespace Atlas.Edge.Configuration;

public sealed class AtlasEdgeOptions
{
    public const string SectionName = "AtlasEdge";
    public const string TransportModeNull = "Null";
    public const string TransportModeHttp = "Http";
    public const string ScannerDiscoveryProviderPlatform = "Platform";
    public const string ScannerDiscoveryProviderMock = "Mock";
    public const string ScannerInventoryPublishModeDisabled = "Disabled";
    public const string ScannerInventoryPublishModeQueueOnly = "QueueOnly";
    public const string ScannerInventoryPublishModeTransport = "Transport";
    public const string ScannerHealthProviderPlatform = "Platform";
    public const string ScannerHealthProviderMock = "Mock";
    public const string ScannerConnectorProviderPlatform = "Platform";
    public const string ScannerConnectorProviderMock = "Mock";
    public const string ScannerEvidenceModePlatform = "Platform";
    public const string ScannerEvidenceModeMock = "Mock";

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

    [Range(30, 3600)]
    public int TokenRefreshLeadTimeSeconds { get; set; } = 300;

    [Range(0, 300)]
    public int TokenClockSkewSeconds { get; set; } = 30;

    [Range(1, 60)]
    public int TokenRefreshRetryBaseSeconds { get; set; } = 2;

    [Range(1, 300)]
    public int TokenRefreshRetryMaxSeconds { get; set; } = 60;

    [Required]
    public string TransportMode { get; set; } = TransportModeNull;

    public string? CredentialStorePath { get; set; }

    [Required]
    public string SiteTimezone { get; set; } = "UTC";

    [Range(1, 86400)]
    public int HeartbeatIntervalSeconds { get; set; } = 60;

    [Range(1, 1000)]
    public int QueueBatchSize { get; set; } = 10;

    public bool ScannerDiscoveryEnabled { get; set; } = true;

    [Required]
    public string ScannerDiscoveryProvider { get; set; } = ScannerDiscoveryProviderPlatform;

    [Range(30, 86400)]
    public int ScannerDiscoveryIntervalSeconds { get; set; } = 300;

    [Range(0, 3600)]
    public int ScannerDiscoveryStartupDelaySeconds { get; set; } = 5;

    [Range(1, 300)]
    public int ScannerDiscoveryProviderTimeoutSeconds { get; set; } = 15;

    public string[] ScannerDiscoveryProviders { get; set; } = ["Wia"];

    [Required]
    public string ScannerInventoryPublishMode { get; set; } = ScannerInventoryPublishModeQueueOnly;

    public bool ScannerHealthEnabled { get; set; } = true;

    [Required]
    public string ScannerHealthProvider { get; set; } = ScannerHealthProviderPlatform;

    public bool ScannerConnectorsEnabled { get; set; }

    [Required]
    public string ScannerConnectorProvider { get; set; } = ScannerConnectorProviderPlatform;

    public bool ScannerEvidenceEnabled { get; set; }

    [Required]
    public string ScannerEvidenceMode { get; set; } = ScannerEvidenceModePlatform;

    public string[] ScannerEvidenceProviders { get; set; } = [];

    public string[] ScannerEvidenceRegistryPaths { get; set; } = [];

    public string[] ScannerEvidenceServiceNames { get; set; } = [];

    public string[] ScannerEvidenceEventLogChannels { get; set; } = [];

    public string[] ScannerEvidenceEventLogProviders { get; set; } = [];

    public string[] ScannerEvidenceLogDirectories { get; set; } = [];

    public string[] ScannerEvidenceLogFiles { get; set; } = [];

    public string[] ScannerEvidenceNetworkTargets { get; set; } = [];

    public bool ScannerEvidenceSnmpEnabled { get; set; }

    public bool ScannerEvidenceAllowTlsBypass { get; set; }

    [Range(1, 104857600)]
    public int ScannerEvidenceMaximumFileSizeBytes { get; set; } = 1048576;

    [Range(1, 1048576)]
    public int ScannerEvidenceMaximumReadBytes { get; set; } = 65536;

    [Required]
    public string EnvironmentName { get; set; } = "Development";
}
