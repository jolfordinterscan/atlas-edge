using System.Collections.Immutable;
using Atlas.Edge.ScannerEvidence;

namespace Atlas.Edge.Tests;

internal static class PatternTestData
{
    public static ScannerEvidenceSnapshot Create(
        string manufacturer = "Acme",
        string model = "ScanPro",
        string firmware = "2.3",
        string driver = "4.1",
        bool usbPresent = true,
        long lifetimePages = 12000,
        long jams = 4,
        long doubleFeeds = 2,
        long transportErrors = 1,
        EvidenceServiceState serviceState = EvidenceServiceState.Running,
        string eventCode = "usb_reset",
        string logErrorCode = "transport_timeout",
        string networkErrorState = "ready",
        bool reverseCollectionOrder = false,
        string? volatileValue = null,
        DateTimeOffset? timestamp = null)
    {
        volatileValue ??= Guid.NewGuid().ToString("D");
        var observedAt = timestamp ?? DateTimeOffset.UtcNow;
        var services = new[]
        {
            new ServiceEvidence(
                "WIA",
                EvidenceValue<EvidenceServiceState>.Known(serviceState),
                EvidenceValue<string>.Known("1.0")),
            new ServiceEvidence(
                "CaptureService",
                EvidenceValue<EvidenceServiceState>.Known(EvidenceServiceState.Running),
                EvidenceValue<string>.Unknown())
        };
        var events = new[]
        {
            new EventEvidence(
                EvidenceEventKind.UsbControllerReset,
                eventCode,
                EvidenceValue<DateTimeOffset>.Known(observedAt),
                EvidenceValue<string>.Known(volatileValue)),
            new EventEvidence(
                EvidenceEventKind.DeviceArrival,
                "device_arrival",
                EvidenceValue<DateTimeOffset>.Known(observedAt.AddMinutes(-1)),
                EvidenceValue<string>.Known(volatileValue + "-arrival"))
        };
        var counters = new[]
        {
            KeyValuePair.Create("lifetime_pages", EvidenceValue<long>.Known(lifetimePages)),
            KeyValuePair.Create("jam_count", EvidenceValue<long>.Known(jams)),
            KeyValuePair.Create("double_feed_count", EvidenceValue<long>.Known(doubleFeeds)),
            KeyValuePair.Create("transport_errors", EvidenceValue<long>.Known(transportErrors))
        };
        var maintenance = new[]
        {
            KeyValuePair.Create("roller_life", EvidenceValue<string>.Known("74%")),
            KeyValuePair.Create("pad_life", EvidenceValue<string>.Known("61%"))
        };
        var logCodes = new[] { logErrorCode, "driver_restart" };

        if (reverseCollectionOrder)
        {
            Array.Reverse(services);
            Array.Reverse(events);
            Array.Reverse(counters);
            Array.Reverse(maintenance);
            Array.Reverse(logCodes);
        }

        var sourceA = Source("provider-a");
        var sourceB = Source("provider-b");
        var observations = new[]
        {
            Observation(sourceA, volatileValue + "-target-a", observedAt),
            Observation(sourceB, volatileValue + "-target-b", observedAt.AddHours(1))
        };
        if (reverseCollectionOrder)
        {
            Array.Reverse(observations);
        }

        return new ScannerEvidenceSnapshot(
            "scanner-" + volatileValue,
            EvidenceValue<DeviceIdentityEvidence>.Known(new DeviceIdentityEvidence(
                EvidenceValue<string>.Known(manufacturer),
                EvidenceValue<string>.Known(model),
                EvidenceValue<string>.Known("serial-" + volatileValue),
                EvidenceValue<string>.Known("hardware-" + volatileValue),
                EvidenceValue<string>.Known("1234"),
                EvidenceValue<string>.Known("5678"))),
            EvidenceValue<DriverEvidence>.Known(new DriverEvidence(
                EvidenceValue<string>.Known("scanner.inf"),
                EvidenceValue<string>.Known(driver),
                EvidenceValue<string>.Known("Acme Drivers"),
                EvidenceValue<DateTimeOffset>.Known(observedAt.AddYears(-1)))),
            EvidenceValue<ConnectionEvidence>.Known(new ConnectionEvidence(
                EvidenceValue<bool>.Known(usbPresent),
                EvidenceValue<string>.Known("usb-path-" + volatileValue),
                EvidenceValue<DateTimeOffset>.Known(observedAt),
                EvidenceValue<DateTimeOffset>.Known(observedAt.AddDays(-1)))),
            EvidenceValue<ImmutableArray<ServiceEvidence>>.Known(services.ToImmutableArray()),
            EvidenceValue<ImmutableArray<EventEvidence>>.Known(events.ToImmutableArray()),
            EvidenceValue<CounterEvidence>.Known(new CounterEvidence(counters.ToImmutableDictionary())),
            EvidenceValue<FirmwareEvidence>.Known(new FirmwareEvidence(EvidenceValue<string>.Known(firmware))),
            EvidenceValue<MaintenanceEvidence>.Known(new MaintenanceEvidence(maintenance.ToImmutableDictionary())),
            EvidenceValue<ImmutableArray<LogEvidenceReference>>.Known(
                [new LogEvidenceReference(
                    "log-" + volatileValue,
                    EvidenceValue<bool>.Known(true),
                    EvidenceValue<DateTimeOffset>.Known(observedAt),
                    EvidenceValue<long>.Known(4096),
                    logCodes.ToImmutableArray())]),
            EvidenceValue<NetworkEvidence>.Known(new NetworkEvidence(
                EvidenceValue<bool>.Known(true),
                EvidenceValue<string>.Known(firmware),
                EvidenceValue<string>.Known("network-serial-" + volatileValue),
                EvidenceValue<CounterEvidence>.Known(new CounterEvidence(
                    ImmutableDictionary<string, EvidenceValue<long>>.Empty.Add(
                        "lifetime_pages",
                        EvidenceValue<long>.Known(lifetimePages)))),
                EvidenceValue<TimeSpan>.Known(TimeSpan.FromHours(observedAt.Hour + 1)),
                EvidenceValue<string>.Known(networkErrorState))),
            observations.ToImmutableArray(),
            observations.Select(observation => observation.Provenance).ToImmutableArray());
    }

    public static ScannerEvidenceSnapshot CreateMinimal(string manufacturer, string model) =>
        UnknownSnapshot() with
        {
            Identity = EvidenceValue<DeviceIdentityEvidence>.Known(new DeviceIdentityEvidence(
                EvidenceValue<string>.Known(manufacturer),
                EvidenceValue<string>.Known(model),
                EvidenceValue<string>.Unknown(),
                EvidenceValue<string>.Unknown(),
                EvidenceValue<string>.Unknown(),
                EvidenceValue<string>.Unknown()))
        };

    public static ScannerEvidenceSnapshot UnknownSnapshot() => new(
        "volatile-scanner-id",
        EvidenceValue<DeviceIdentityEvidence>.Unknown(),
        EvidenceValue<DriverEvidence>.Unknown(),
        EvidenceValue<ConnectionEvidence>.Unknown(),
        EvidenceValue<ImmutableArray<ServiceEvidence>>.Unknown(),
        EvidenceValue<ImmutableArray<EventEvidence>>.Unknown(),
        EvidenceValue<CounterEvidence>.Unknown(),
        EvidenceValue<FirmwareEvidence>.Unknown(),
        EvidenceValue<MaintenanceEvidence>.Unknown(),
        EvidenceValue<ImmutableArray<LogEvidenceReference>>.Unknown(),
        EvidenceValue<NetworkEvidence>.Unknown(),
        ImmutableArray<ScannerEvidenceObservation>.Empty,
        ImmutableArray<EvidenceProvenance>.Empty);

    private static EvidenceSourceDescriptor Source(string id) => new(
        id,
        id,
        "test",
        EvidenceSourceQuality.OperatingSystem,
        false,
        ImmutableArray<EvidenceCapability>.Empty);

    private static ScannerEvidenceObservation Observation(
        EvidenceSourceDescriptor source,
        string targetId,
        DateTimeOffset observedAt) =>
        new(
            source,
            new ScannerEvidenceTarget(targetId, source.ProviderId, ImmutableArray<EvidenceCorrelationKey>.Empty),
            observedAt,
            new EvidenceProvenance(source.ProviderId, source.SourceType, source.SourceQuality, targetId),
            EvidenceValue<DeviceIdentityEvidence>.Unknown(),
            EvidenceValue<DriverEvidence>.Unknown(),
            EvidenceValue<ConnectionEvidence>.Unknown(),
            EvidenceValue<ImmutableArray<ServiceEvidence>>.Unknown(),
            EvidenceValue<ImmutableArray<EventEvidence>>.Unknown(),
            EvidenceValue<CounterEvidence>.Unknown(),
            EvidenceValue<FirmwareEvidence>.Unknown(),
            EvidenceValue<MaintenanceEvidence>.Unknown(),
            EvidenceValue<ImmutableArray<LogEvidenceReference>>.Unknown(),
            EvidenceValue<NetworkEvidence>.Unknown());
}
