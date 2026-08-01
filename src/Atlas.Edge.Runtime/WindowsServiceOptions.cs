namespace Atlas.Edge.Runtime;

public sealed class WindowsServiceOptions
{
    public const string SectionName = "WindowsService";

    public string ServiceName { get; set; } = "Atlas Edge Runtime";

    public string DisplayName { get; set; } = "Atlas Edge Runtime";

    public string Description { get; set; } = "Outbound-only Atlas Edge runtime.";

    public string EventLogSourceName { get; set; } = "Atlas Edge Runtime";

    public int HealthHeartbeatIntervalSeconds { get; set; } = 30;

    public LocalServiceConfigurationOptions LocalConfiguration { get; set; } = new();
}

public sealed class LocalServiceConfigurationOptions
{
    public bool Enabled { get; set; }

    public string? Path { get; set; }
}
