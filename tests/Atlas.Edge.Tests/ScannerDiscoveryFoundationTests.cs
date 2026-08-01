using System.Text.Json;
using Atlas.Edge.Core;
using Atlas.Edge.Queue;
using Atlas.Edge.ScannerDiscovery;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atlas.Edge.Tests;

public sealed class ScannerDiscoveryFoundationTests
{
    [Fact]
    public void Identity_IsStableAndNeverContainsRawProviderIdentifier()
    {
        var factory = new ScannerIdentityFactory();
        var scanner = Device(sourceId: @"\\?\usb#vid_1234&pid_5678#private-path");

        var first = factory.Create(scanner);
        var second = factory.Create(scanner);

        Assert.Equal(first, second);
        Assert.StartsWith("scanner-", first.ScannerId, StringComparison.Ordinal);
        Assert.DoesNotContain("private-path", first.ScannerId, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-path", first.ProviderId, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Identity_UsesSerialThenDevicePathThenDeterministicMetadataFallback()
    {
        var factory = new ScannerIdentityFactory();
        var serial = factory.Create(Device(sourceId: "", serial: "SERIAL-42"));
        var path = factory.Create(Device(sourceId: "", devicePath: "USB#PRIVATE-PATH"));
        var fallbackScanner = Device(sourceId: "");
        var fallback = factory.Create(fallbackScanner);

        Assert.Equal(ScannerMetadataConfidence.SerialIdentity, serial.Confidence);
        Assert.Equal(ScannerMetadataConfidence.DevicePathIdentity, path.Confidence);
        Assert.Equal(ScannerMetadataConfidence.MetadataFallback, fallback.Confidence);
        Assert.Equal(fallback, factory.Create(fallbackScanner));
        Assert.DoesNotContain("PRIVATE", path.ScannerId, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Discovery_DoesNotMergeSameModelWithDifferentSerials()
    {
        var service = Service(
            new StaticAdapter(ScannerProtocol.Wia, [Device(serial: "SERIAL-A"), Device(sourceId: "two", serial: "SERIAL-B")]));

        var snapshot = await service.DiscoverAsync(CancellationToken.None);

        Assert.Equal(2, snapshot.Scanners.Count);
        Assert.Equal(2, snapshot.Scanners.Select(scanner => scanner.DiscoveryId).Distinct().Count());
    }

    [Fact]
    public async Task Discovery_TimesOutOneProviderAndKeepsAnotherProvider()
    {
        var service = new ScannerDiscoveryService(
            [new NeverCompletingAdapter(), new StaticAdapter(ScannerProtocol.Wia, [Device()])],
            TimeProvider.System,
            NullLogger<ScannerDiscoveryService>.Instance,
            new ScannerIdentityFactory(),
            TimeSpan.FromMilliseconds(20));

        var snapshot = await service.DiscoverAsync(CancellationToken.None);

        Assert.Single(snapshot.Scanners);
        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.ErrorCode == "provider_timeout");
    }

    [Fact]
    public void InventoryFingerprint_IgnoresObservationTimesButChangesWithMetadataAndRemoval()
    {
        var builder = new ScannerInventoryEventBuilder();
        var scanner = Discovered("Model A", DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var later = scanner with
        {
            FirstObservedUtc = DateTimeOffset.Parse("2026-02-01T00:00:00Z"),
            LastObservedUtc = DateTimeOffset.Parse("2026-02-02T00:00:00Z")
        };

        var first = Snapshot(scanner);
        var second = Snapshot(later);
        var changed = Snapshot(later with { Model = "Model B" });

        Assert.Equal(builder.Fingerprint(first), builder.Fingerprint(second));
        Assert.NotEqual(builder.Fingerprint(second), builder.Fingerprint(changed));
        Assert.NotEqual(builder.Fingerprint(second), builder.Fingerprint(Snapshot()));
    }

    [Fact]
    public async Task Queue_CoalescesInventoryAndHeartbeatBatchNeverContainsInventory()
    {
        var queue = new InMemoryEventQueue();
        var builder = new ScannerInventoryEventBuilder();
        var identity = new AgentIdentity(
            "agent", "workstation", "tenant", "Test", false, DateTimeOffset.UtcNow);
        var inventory = builder.Build(Snapshot(Discovered("Model A", DateTimeOffset.UtcNow)), identity);

        var first = await queue.EnqueueInventoryAsync(inventory, CancellationToken.None);
        var duplicate = await queue.EnqueueInventoryAsync(
            inventory with { EventId = "different-event-id" },
            CancellationToken.None);
        await queue.EnqueueAsync(Heartbeat(), CancellationToken.None);

        Assert.True(first.WasQueued);
        Assert.False(duplicate.WasQueued);
        Assert.Equal(first.ReceiptId, duplicate.ReceiptId);
        Assert.Single(await queue.PeekBatchAsync(10, CancellationToken.None));
        Assert.Equal(inventory.InventoryVersion,
            (await queue.GetLatestInventoryAsync(CancellationToken.None))!.InventoryVersion);
    }

    [Fact]
    public void InventoryEvent_HasVersionedContractAndNoRawDevicePath()
    {
        var identity = new AgentIdentity(
            "agent", "workstation", "tenant", "Test", false, DateTimeOffset.UtcNow);
        var scanner = Discovered("Model A", DateTimeOffset.UtcNow) with
        {
            DevicePathHash = "0123456789abcdef",
            ProviderId = "provider-hash"
        };

        var inventory = new ScannerInventoryEventBuilder().Build(Snapshot(scanner), identity);
        var json = JsonSerializer.Serialize(inventory);

        Assert.Equal("scanner.inventory", inventory.EventType);
        Assert.Equal("1.0", inventory.SchemaVersion);
        Assert.Equal(1, inventory.ScannerCount);
        Assert.DoesNotContain("private-device-path", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("enrollment", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WiaProvider_HasNoAcquisitionDialogOrScannerCommandSurface()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Atlas.Edge.ScannerDiscovery",
            "WiaScannerSourceCatalog.cs"));

        Assert.DoesNotContain(".Connect(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowAcquireImage", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowSelectDevice", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Transfer(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartScan", source, StringComparison.Ordinal);
    }

    private static ScannerDiscoveryService Service(params IScannerDiscoveryAdapter[] adapters) =>
        new(adapters, TimeProvider.System, NullLogger<ScannerDiscoveryService>.Instance);

    private static AdapterScannerDevice Device(
        string sourceId = "source-one",
        string? serial = null,
        string? devicePath = null) =>
        new(
            sourceId,
            ScannerProtocol.Wia,
            "RICOH",
            "Test Scanner",
            serial,
            null,
            "USB",
            null,
            null,
            null,
            [],
            new ScannerDriver("WIA Driver", "1.0", "RICOH"),
            ScannerOnlineStatus.Unknown)
        {
            DevicePath = devicePath
        };

    private static DiscoveredScanner Discovered(string model, DateTimeOffset observedAt) =>
        new(
            "scanner-0123456789abcdef01234567",
            "RICOH",
            model,
            "SERIAL-42",
            null,
            "USB",
            null,
            null,
            null,
            [],
            [new ScannerDriver("WIA Driver", "1.0", "RICOH")],
            ScannerOnlineStatus.Unknown,
            [ScannerProtocol.Wia])
        {
            ProviderId = "provider-hash",
            ProviderName = "Wia",
            ConnectionType = ScannerConnectionType.Usb,
            Status = ScannerOperationalStatus.Unknown,
            MetadataConfidence = ScannerMetadataConfidence.ProviderStableIdentity,
            FirstObservedUtc = observedAt,
            LastObservedUtc = observedAt
        };

    private static ScannerDiscoverySnapshot Snapshot(params DiscoveredScanner[] scanners) =>
        new(DateTimeOffset.UtcNow, scanners, []);

    private static AgentHeartbeatEvent Heartbeat() =>
        new(
            "heartbeat-id",
            "agent.heartbeat",
            "1.0",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "agent",
            "workstation",
            "tenant",
            "test",
            null,
            "Test");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Atlas.Edge.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed class StaticAdapter : IScannerDiscoveryAdapter
    {
        private readonly IReadOnlyList<AdapterScannerDevice> _devices;

        public StaticAdapter(ScannerProtocol protocol, IReadOnlyList<AdapterScannerDevice> devices)
        {
            Protocol = protocol;
            _devices = devices;
        }

        public ScannerProtocol Protocol { get; }

        public Task<ScannerAdapterResult> DiscoverAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ScannerAdapterResult.Available(Protocol, _devices));
    }

    private sealed class NeverCompletingAdapter : IScannerDiscoveryAdapter
    {
        public ScannerProtocol Protocol => ScannerProtocol.Twain;

        public Task<ScannerAdapterResult> DiscoverAsync(CancellationToken cancellationToken) =>
            new TaskCompletionSource<ScannerAdapterResult>().Task;
    }
}
