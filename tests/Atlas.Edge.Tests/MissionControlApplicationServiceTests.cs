using System.Collections.Immutable;
using System.Text.Json;
using Atlas.Edge.Patterns;
using Atlas.Edge.Runtime;
using Atlas.Edge.ScannerConnectors;
using Atlas.Edge.ScannerDiscovery;
using Atlas.Edge.ScannerEvidence;
using Atlas.Edge.ScannerHealth;

namespace Atlas.Edge.Tests;

public sealed class MissionControlApplicationServiceTests
{
    [Fact]
    public void Read_CombinesCurrentStatesAndCalculatesTenantScopedFleetSummary()
    {
        var fixture = CreateFixture();

        var view = fixture.Service.Read(new MissionControlTenantScope("tenant-7", "Northwind", "Seattle"));

        Assert.Equal("tenant-7", view.TenantId);
        Assert.Equal("Northwind", view.TenantName);
        Assert.Equal(1, view.Summary.TotalScanners);
        Assert.Equal(1, view.Summary.OnlineScanners);
        Assert.Equal(1, view.Summary.HealthyScanners);
        Assert.Equal(92, view.Summary.FleetConfidence);
        Assert.Equal(2, view.Summary.EvidenceObservations);
        Assert.Equal(1, view.Summary.PatternsIdentified);
        var scanner = Assert.Single(view.Scanners);
        Assert.Equal("Seattle", scanner.SiteName);
        Assert.Equal(92, scanner.HealthScore);
        Assert.Equal(MissionControlHealthBand.Healthy, scanner.HealthBand);
        Assert.Contains("Mock", scanner.ConnectorSources);
        Assert.NotNull(scanner.Pattern);
        Assert.Equal("ExactMatch", scanner.Pattern!.MatchLevel);
        Assert.Equal(100, scanner.Pattern.SimilarityScore);
        Assert.All(view.Subsystems, subsystem => Assert.Equal(MissionControlSubsystemState.Available, subsystem.State));
    }

    [Fact]
    public void Read_PreservesEvidenceStatesAndNeverExposesSensitiveDeviceIdentifiers()
    {
        var fixture = CreateFixture();

        var view = fixture.Service.Read(new MissionControlTenantScope("tenant-7", "Northwind"));
        var scanner = Assert.Single(view.Scanners);

        Assert.Contains(scanner.Evidence, field => field.State == EvidenceValueState.Known.ToString());
        Assert.Contains(scanner.Evidence, field => field.State == EvidenceValueState.Unknown.ToString());
        Assert.Contains(scanner.Evidence, field => field.State == EvidenceValueState.Unsupported.ToString());
        Assert.Contains(scanner.Provenance, source => source.Contains("StandardProtocol", StringComparison.Ordinal));
        var serialized = JsonSerializer.Serialize(view);
        Assert.DoesNotContain("SECRET-SERIAL", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hardware-SECRET", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("target-SECRET", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_RendersEmptyAndPartialSubsystemStatesWithoutInventingHealth()
    {
        var inventory = new ScannerInventoryState();
        inventory.Update(new ScannerDiscoverySnapshot(
            DateTimeOffset.UtcNow,
            Array.Empty<DiscoveredScanner>(),
            [new ScannerAdapterDiagnostic(ScannerProtocol.Wia, false, 0, "wia_runtime_unavailable")]));
        var service = new MissionControlApplicationService(
            inventory,
            new ScannerHealthState(),
            new ScannerConnectorState(),
            new ScannerEvidenceState(),
            new PatternEngine(),
            TimeProvider.System);

        var view = service.Read(new MissionControlTenantScope("tenant-7", "Northwind"));

        Assert.Empty(view.Scanners);
        Assert.Equal(0, view.Summary.TotalScanners);
        Assert.Null(view.Summary.FleetConfidence);
        Assert.Contains(view.Subsystems, subsystem =>
            subsystem.Name == "Discovery" && subsystem.State == MissionControlSubsystemState.PartialFailure);
        Assert.Contains(view.Subsystems, subsystem =>
            subsystem.Name == "Health" && subsystem.State == MissionControlSubsystemState.Unavailable);
        Assert.Contains(view.Subsystems, subsystem =>
            subsystem.Name == "Evidence" && subsystem.DisplayMessage == "Not yet available");
    }

    [Fact]
    public void Read_RequiresExplicitTenantScopeAndCountsEachEvidenceSnapshotOnce()
    {
        var fixture = CreateFixture();
        Assert.Throws<ArgumentException>(() =>
            fixture.Service.Read(new MissionControlTenantScope(string.Empty, "Northwind")));

        var first = fixture.Service.Read(new MissionControlTenantScope("tenant-7", "Northwind"));
        var repeated = fixture.Service.Read(new MissionControlTenantScope("tenant-7", "Northwind"));
        Assert.Equal(1, first.Scanners[0].Pattern!.OccurrenceCount);
        Assert.Equal(1, repeated.Scanners[0].Pattern!.OccurrenceCount);

        fixture.Evidence.Update(fixture.Evidence.Current! with
        {
            CollectedAtUtc = fixture.Evidence.Current!.CollectedAtUtc.AddMinutes(1)
        });
        var updated = fixture.Service.Read(new MissionControlTenantScope("tenant-7", "Northwind"));
        Assert.Equal(2, updated.Scanners[0].Pattern!.OccurrenceCount);
    }

    [Fact]
    public void DevelopmentAdapterPatternId_RemainsCompatibleWithPatternEngine()
    {
        var evidence = PatternTestData.UnknownSnapshot() with
        {
            Identity = EvidenceValue<DeviceIdentityEvidence>.Known(new DeviceIdentityEvidence(
                EvidenceValue<string>.Known("Atlas Mock Devices"),
                EvidenceValue<string>.Known("Document Scanner"),
                EvidenceValue<string>.Unknown(),
                EvidenceValue<string>.Unknown(),
                EvidenceValue<string>.Unknown(),
                EvidenceValue<string>.Unknown())),
            Driver = EvidenceValue<DriverEvidence>.Known(new DriverEvidence(
                EvidenceValue<string>.Known("Atlas Mock Scanner Driver"),
                EvidenceValue<string>.Known("0.0-mock"),
                EvidenceValue<string>.Unknown(),
                EvidenceValue<DateTimeOffset>.Unknown())),
            Connection = EvidenceValue<ConnectionEvidence>.Known(new ConnectionEvidence(
                EvidenceValue<bool>.Known(true),
                EvidenceValue<string>.Unknown(),
                EvidenceValue<DateTimeOffset>.Unknown(),
                EvidenceValue<DateTimeOffset>.Unknown())),
            Counters = EvidenceValue<CounterEvidence>.Known(new CounterEvidence(
                ImmutableDictionary<string, EvidenceValue<long>>.Empty
                    .Add("lifetime_pages", EvidenceValue<long>.Known(125000))
                    .Add("daily_pages", EvidenceValue<long>.Known(450))
                    .Add("jam_count", EvidenceValue<long>.Known(1))
                    .Add("double_feed_count", EvidenceValue<long>.Known(0))
                    .Add("transport_error_count", EvidenceValue<long>.Known(0)))),
            Firmware = EvidenceValue<FirmwareEvidence>.Known(new FirmwareEvidence(
                EvidenceValue<string>.Known("0.0-mock"))),
            Maintenance = EvidenceValue<MaintenanceEvidence>.Known(new MaintenanceEvidence(
                ImmutableDictionary<string, EvidenceValue<string>>.Empty.Add(
                    "cleaning-cycles",
                    EvidenceValue<string>.Known("12"))))
        };

        var fingerprint = new PatternEngine().Fingerprint(evidence);

        Assert.Equal("PAT-76874942991962433839178258131", fingerprint.PatternId.Value);
    }

    [Fact]
    public void Read_MissingHealthPreservesUnknownHealthWithoutBlockingTheView()
    {
        var now = new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero);
        var inventory = new ScannerInventoryState();
        inventory.Update(new ScannerDiscoverySnapshot(
            now,
            [new DiscoveredScanner(
                "scanner-unknown-health",
                "Acme",
                "ScanPro",
                null,
                null,
                "USB",
                false,
                false,
                false,
                ImmutableArray<string>.Empty,
                ImmutableArray<ScannerDriver>.Empty,
                ScannerOnlineStatus.Unknown,
                [ScannerProtocol.Wia])],
            ImmutableArray<ScannerAdapterDiagnostic>.Empty));
        var service = new MissionControlApplicationService(
            inventory,
            new ScannerHealthState(),
            new ScannerConnectorState(),
            new ScannerEvidenceState(),
            new PatternEngine(),
            new ManualTimeProvider(now));

        var view = service.Read(new MissionControlTenantScope("tenant-7", "Northwind"));

        var scanner = Assert.Single(view.Scanners);
        Assert.Null(scanner.HealthScore);
        Assert.Equal(MissionControlHealthBand.Unknown, scanner.HealthBand);
        Assert.Equal(1, view.Summary.UnknownHealth);
        Assert.Null(view.Summary.FleetConfidence);
        Assert.Contains(view.Subsystems, subsystem =>
            subsystem.Name == "Health" && subsystem.State == MissionControlSubsystemState.Unavailable);
    }

    [Fact]
    public void Read_DoesNotGeneratePatternWithoutCorrelatableMeaningfulEvidence()
    {
        var fixture = CreateFixture();
        fixture.Evidence.Update(new ScannerEvidenceCollectionSnapshot(
            fixture.Evidence.Current!.CollectedAtUtc.AddMinutes(1),
            [PatternTestData.UnknownSnapshot()],
            ImmutableArray<EvidenceProviderDiagnostic>.Empty));

        var view = fixture.Service.Read(new MissionControlTenantScope("tenant-7", "Northwind"));

        var scanner = Assert.Single(view.Scanners);
        Assert.Null(scanner.Pattern);
        Assert.Equal(0, view.Summary.PatternsIdentified);
        Assert.Equal(1, view.Summary.UnknownPatterns);
    }

    [Fact]
    public void MissionControlSource_HasNoQueueTransportEnrollmentKnowledgeCommandOrPersistenceSurface()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Atlas.Edge.Runtime",
            "MissionControlApplicationService.cs"));
        var forbidden = new[]
        {
            "Atlas.Edge.Queue",
            "Atlas.Edge.Transport",
            "Atlas.Edge.Enrollment",
            "Atlas.Edge.Knowledge",
            "ScannerCommand",
            "HttpClient",
            "HttpListener",
            "File.Write",
            "Database",
            "Persist",
            "Enqueue",
            "Transmit"
        };

        Assert.All(forbidden, term => Assert.DoesNotContain(term, source, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            "Atlas.Edge.Runtime",
            typeof(PatternEngine).Assembly.GetReferencedAssemblies().Select(reference => reference.Name));
    }

    private static Fixture CreateFixture()
    {
        var now = new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero);
        var time = new ManualTimeProvider(now);
        var inventory = new ScannerInventoryState();
        var health = new ScannerHealthState();
        var connectors = new ScannerConnectorState();
        var evidenceState = new ScannerEvidenceState();
        inventory.Update(new ScannerDiscoverySnapshot(
            now,
            [new DiscoveredScanner(
                "scanner-1",
                "Acme",
                "ScanPro",
                "SECRET-SERIAL",
                "2.3",
                "USB",
                true,
                true,
                true,
                ["duplex"],
                [new ScannerDriver("scanner.inf", "4.1", "Acme")],
                ScannerOnlineStatus.Online,
                [ScannerProtocol.Mock])],
            [new ScannerAdapterDiagnostic(ScannerProtocol.Mock, true, 1, null)]));
        health.Update(new ScannerHealthCollectionSnapshot(
            now,
            [new ScannerHealthSnapshot(
                "scanner-1",
                now,
                "Acme",
                "ScanPro",
                "SECRET-SERIAL",
                12000,
                450,
                72,
                64,
                ImmutableArray<ScannerConsumableHealth>.Empty,
                false,
                ImmutableDictionary<string, long>.Empty,
                false,
                "2.3",
                58,
                60,
                1,
                0,
                0,
                ScannerOnlineStatus.Online,
                ScannerDriverHealthStatus.Ready,
                new ScannerUsbStability(0, null),
                TimeSpan.FromHours(72),
                [ScannerProtocol.Mock],
                new ScannerHealthScore(69, 100, 97, 100, 92))],
            [new ScannerHealthProviderDiagnostic(ScannerProtocol.Mock, true, 1, null)]));
        var descriptor = new ConnectorDescriptor(
            "development_mock",
            "Development Mock Scanner Connector",
            "Mock",
            null,
            true,
            [ConnectorCapability.Discovery]);
        connectors.Update(new ScannerConnectorCollectionSnapshot(
            now,
            [new ScannerConnectorSnapshot(
                "scanner-1",
                ConnectorValue<ScannerIdentity>.Unsupported(),
                ConnectorValue<ScannerCapabilities>.Unsupported(),
                ConnectorValue<ScannerFirmware>.Unsupported(),
                ConnectorValue<ScannerCounters>.Unsupported(),
                ConnectorValue<Atlas.Edge.ScannerConnectors.ScannerHealth>.Unsupported(),
                ConnectorValue<ScannerStatus>.Unsupported(),
                ConnectorValue<ScannerDiagnostics>.Unsupported(),
                ConnectorValue<ImmutableArray<ScannerLogReference>>.Unsupported(),
                ImmutableArray<ScannerConnectorObservation>.Empty,
                [descriptor])],
            ImmutableArray<ConnectorDiagnostic>.Empty));
        var evidence = PatternTestData.Create(volatileValue: "SECRET");
        evidence = evidence with
        {
            Identity = EvidenceValue<DeviceIdentityEvidence>.Known(new DeviceIdentityEvidence(
                EvidenceValue<string>.Known("Acme"),
                EvidenceValue<string>.Known("ScanPro"),
                EvidenceValue<string>.Known("SECRET-SERIAL"),
                EvidenceValue<string>.Known("hardware-SECRET"),
                EvidenceValue<string>.Known("1234"),
                EvidenceValue<string>.Known("5678"))),
            Driver = EvidenceValue<DriverEvidence>.Known(new DriverEvidence(
                EvidenceValue<string>.Known("scanner.inf"),
                EvidenceValue<string>.Known("4.1"),
                EvidenceValue<string>.Unknown(),
                EvidenceValue<DateTimeOffset>.Unknown())),
            Services = EvidenceValue<ImmutableArray<ServiceEvidence>>.Unsupported(),
            Network = EvidenceValue<NetworkEvidence>.Unsupported(),
            Provenance = [new EvidenceProvenance(
                "provider-safe",
                "Mock",
                EvidenceSourceQuality.StandardProtocol,
                "target-SECRET")]
        };
        evidenceState.Update(new ScannerEvidenceCollectionSnapshot(
            now,
            [evidence],
            ImmutableArray<EvidenceProviderDiagnostic>.Empty));
        return new Fixture(
            new MissionControlApplicationService(
                inventory,
                health,
                connectors,
                evidenceState,
                new PatternEngine(time),
                time),
            evidenceState);
    }

    private sealed record Fixture(
        MissionControlApplicationService Service,
        ScannerEvidenceState Evidence);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Atlas.Edge.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
