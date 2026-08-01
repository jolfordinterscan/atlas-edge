using System.Collections.Immutable;
using Atlas.Edge.ScannerEvidence;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atlas.Edge.Tests;

public sealed class ScannerEvidenceManagerTests
{
    [Fact]
    public void EvidenceValue_PreservesAllFiveStatesWithoutDefaults()
    {
        var known = EvidenceValue<long>.Known(7);
        var unknown = EvidenceValue<long>.Unknown();
        var unsupported = EvidenceValue<long>.Unsupported();
        var unavailable = EvidenceValue<long>.Unavailable();
        var failed = EvidenceValue<long>.Failed("Raw exception detail");

        Assert.Equal(EvidenceValueState.Known, known.State);
        Assert.Equal(7, known.Value);
        Assert.Equal(EvidenceValueState.Unknown, unknown.State);
        Assert.Equal(EvidenceErrorCodes.DataUnknown, unknown.ErrorCode);
        Assert.Equal(EvidenceValueState.Unsupported, unsupported.State);
        Assert.Equal(EvidenceValueState.Unavailable, unavailable.State);
        Assert.Equal(EvidenceValueState.Failed, failed.State);
        Assert.Equal(EvidenceErrorCodes.CollectionFailed, failed.ErrorCode);
        Assert.Throws<InvalidOperationException>(() => unknown.Value);
    }

    [Fact]
    public async Task Collect_InvokesOnlyDeclaredCapabilities()
    {
        var provider = new TestProvider(
            "limited",
            Identity("Acme", "ScanPro", null),
            ImmutableArray<EvidenceCorrelationKey>.Empty,
            EvidenceCapability.DeviceIdentity);
        var manager = CreateManager(provider);

        var snapshot = Assert.Single((await manager.CollectAsync(CancellationToken.None)).Scanners);

        Assert.Equal(1, provider.IdentityCalls);
        Assert.Equal(0, provider.DriverCalls);
        Assert.Equal(EvidenceValueState.Known, snapshot.Identity.State);
        Assert.Equal(EvidenceValueState.Unsupported, snapshot.Driver.State);
    }

    [Fact]
    public async Task Collect_IsolatesProviderFailuresAndUsesStableCodes()
    {
        var unavailable = new TestProvider(
            "unavailable",
            Identity("Acme", "Unavailable", "SERIAL-0"),
            ImmutableArray<EvidenceCorrelationKey>.Empty,
            EvidenceCapability.DeviceIdentity)
        {
            Availability = EvidenceAvailability.Unavailable("platform_unavailable")
        };
        var throwing = new TestProvider(
            "throwing",
            Identity("Acme", "Throwing", "SECRET-SERIAL"),
            ImmutableArray<EvidenceCorrelationKey>.Empty,
            EvidenceCapability.DeviceIdentity)
        {
            ThrowIdentity = true
        };
        var healthy = new TestProvider(
            "healthy",
            Identity("Acme", "Healthy", "SERIAL-2"),
            ImmutableArray<EvidenceCorrelationKey>.Empty,
            EvidenceCapability.DeviceIdentity);

        var collection = await CreateManager(unavailable, throwing, healthy).CollectAsync(CancellationToken.None);

        Assert.Equal(2, collection.Scanners.Length);
        Assert.Contains(collection.Diagnostics, diagnostic =>
            diagnostic.ProviderId == "throwing" &&
            diagnostic.Operation == "identity" &&
            diagnostic.ErrorCode == EvidenceErrorCodes.CollectionFailed);
        Assert.Contains(collection.Diagnostics, diagnostic =>
            diagnostic.ProviderId == "unavailable" && diagnostic.State == EvidenceValueState.Unavailable);
    }

    [Fact]
    public async Task Collect_CorrelatesStrongIdentitiesButLeavesAmbiguousModelsSeparate()
    {
        var hardwareHash = "shared_hardware_hash";
        var manager = CreateManager(
            new TestProvider(
                "pnp",
                Identity("Acme", "Same Model", "SERIAL-1"),
                [new EvidenceCorrelationKey(EvidenceCorrelationKind.HardwareInstance, hardwareHash)],
                EvidenceCapability.DeviceIdentity),
            new TestProvider(
                "driver",
                Identity("Unknown", "Same Model", null),
                [new EvidenceCorrelationKey(EvidenceCorrelationKind.HardwareInstance, hardwareHash)],
                EvidenceCapability.DeviceIdentity),
            new TestProvider(
                "protocol_a",
                Identity("Acme", "Same Model", "SERIAL-1"),
                ImmutableArray<EvidenceCorrelationKey>.Empty,
                EvidenceCapability.DeviceIdentity),
            new TestProvider(
                "protocol_b",
                Identity("Acme", "Same Model", "SERIAL-1"),
                ImmutableArray<EvidenceCorrelationKey>.Empty,
                EvidenceCapability.DeviceIdentity),
            new TestProvider(
                "ambiguous_a",
                Identity("Acme", "Same Model", null),
                ImmutableArray<EvidenceCorrelationKey>.Empty,
                EvidenceCapability.DeviceIdentity),
            new TestProvider(
                "ambiguous_b",
                Identity("Acme", "Same Model", null),
                ImmutableArray<EvidenceCorrelationKey>.Empty,
                EvidenceCapability.DeviceIdentity));

        var collection = await manager.CollectAsync(CancellationToken.None);

        Assert.Equal(3, collection.Scanners.Length);
        Assert.Single(collection.Scanners.Where(scanner => scanner.Observations.Length == 4));
        Assert.Equal(2, collection.Scanners.Count(scanner => scanner.Observations.Length == 1));
        Assert.All(collection.Scanners, scanner =>
        {
            Assert.StartsWith("evidence-", scanner.ScannerId, StringComparison.Ordinal);
            Assert.DoesNotContain("SERIAL-1", scanner.ScannerId, StringComparison.Ordinal);
            Assert.Equal(scanner.Observations.Length, scanner.Provenance.Length);
        });
    }

    [Fact]
    public async Task Collect_ReturnsImmutableSnapshotsWithSourceQualityProvenance()
    {
        var collection = await CreateManager(new TestProvider(
            "protocol",
            Identity("Acme", "ScanPro", "SERIAL-1"),
            ImmutableArray<EvidenceCorrelationKey>.Empty,
            EvidenceCapability.DeviceIdentity)).CollectAsync(CancellationToken.None);
        var changed = collection.Scanners.Add(collection.Scanners[0]);

        var snapshot = Assert.Single(collection.Scanners);
        Assert.Equal(2, changed.Length);
        Assert.Single(snapshot.Observations);
        var provenance = Assert.Single(snapshot.Provenance);
        Assert.Equal(EvidenceSourceQuality.StandardProtocol, provenance.SourceQuality);
    }

    private static ScannerEvidenceManager CreateManager(params IScannerEvidenceProvider[] providers) =>
        new(providers, new ManualTimeProvider(DateTimeOffset.UtcNow), NullLogger<ScannerEvidenceManager>.Instance);

    private static DeviceIdentityEvidence Identity(string manufacturer, string model, string? serial) =>
        new(
            EvidenceValue<string>.Known(manufacturer),
            EvidenceValue<string>.Known(model),
            string.IsNullOrWhiteSpace(serial) ? EvidenceValue<string>.Unknown() : EvidenceValue<string>.Known(serial),
            EvidenceValue<string>.Unknown(),
            EvidenceValue<string>.Unknown(),
            EvidenceValue<string>.Unknown());

    private sealed class TestProvider : ScannerEvidenceProviderBase
    {
        private readonly DeviceIdentityEvidence _identity;
        private readonly ImmutableArray<EvidenceCorrelationKey> _correlations;

        public TestProvider(
            string providerId,
            DeviceIdentityEvidence identity,
            ImmutableArray<EvidenceCorrelationKey> correlations,
            params EvidenceCapability[] capabilities)
        {
            _identity = identity;
            _correlations = correlations;
            Descriptor = new EvidenceSourceDescriptor(
                providerId,
                providerId,
                "test",
                EvidenceSourceQuality.StandardProtocol,
                false,
                capabilities.Prepend(EvidenceCapability.Discovery).Distinct().ToImmutableArray());
        }

        public override EvidenceSourceDescriptor Descriptor { get; }

        public EvidenceAvailability Availability { get; init; } = EvidenceAvailability.Available();

        public bool ThrowIdentity { get; init; }

        public int IdentityCalls { get; private set; }

        public int DriverCalls { get; private set; }

        public override Task<EvidenceAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Availability);

        public override Task<EvidenceValue<ImmutableArray<ScannerEvidenceTarget>>> DiscoverTargetsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(EvidenceValue<ImmutableArray<ScannerEvidenceTarget>>.Known(
                [new ScannerEvidenceTarget($"target-{Descriptor.ProviderId}", Descriptor.ProviderId, _correlations)]));

        public override Task<EvidenceValue<DeviceIdentityEvidence>> ReadIdentityAsync(
            ScannerEvidenceTarget target,
            CancellationToken cancellationToken)
        {
            IdentityCalls++;
            return ThrowIdentity
                ? throw new InvalidOperationException("SECRET-SERIAL raw exception")
                : Task.FromResult(EvidenceValue<DeviceIdentityEvidence>.Known(_identity));
        }

        public override Task<EvidenceValue<DriverEvidence>> ReadDriverAsync(
            ScannerEvidenceTarget target,
            CancellationToken cancellationToken)
        {
            DriverCalls++;
            return Task.FromResult(EvidenceValue<DriverEvidence>.Unknown());
        }
    }
}
