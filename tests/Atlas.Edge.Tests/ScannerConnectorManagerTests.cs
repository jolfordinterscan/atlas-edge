using System.Collections.Immutable;
using Atlas.Edge.ScannerConnectors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ConnectorScannerHealth = Atlas.Edge.ScannerConnectors.ScannerHealth;

namespace Atlas.Edge.Tests;

public sealed class ScannerConnectorManagerTests
{
    [Fact]
    public async Task Collect_InvokesOnlyDeclaredCapabilitiesAndPreservesResultStates()
    {
        var connector = new TestConnector(
            "limited",
            Identity("Acme", "ScanPro", serial: null),
            ConnectorCapability.Identity,
            ConnectorCapability.Health)
        {
            HealthResult = ConnectorValue<ConnectorScannerHealth>.Unknown()
        };
        var manager = CreateManager(connector);

        var snapshot = Assert.Single((await manager.CollectAsync(CancellationToken.None)).Scanners);

        Assert.Equal(1, connector.IdentityCalls);
        Assert.Equal(1, connector.HealthCalls);
        Assert.Equal(0, connector.FirmwareCalls);
        Assert.Equal(ConnectorResultState.Known, snapshot.Identity.State);
        Assert.Equal(ConnectorResultState.Unknown, snapshot.Health.State);
        Assert.Equal(ConnectorErrorCodes.DataUnknown, snapshot.Health.ErrorCode);
        Assert.Equal(ConnectorResultState.Unsupported, snapshot.Firmware.State);
        Assert.Equal(ConnectorErrorCodes.CapabilityUnsupported, snapshot.Firmware.ErrorCode);
    }

    [Fact]
    public async Task Collect_IsolatesAvailabilityAndReadFailuresWithStableCodes()
    {
        var unavailable = new TestConnector(
            "unavailable",
            Identity("Acme", "One", "SERIAL-1"),
            ConnectorCapability.Identity)
        {
            Availability = ConnectorAvailability.Unavailable("wia_runtime_unavailable")
        };
        var throwingAvailability = new TestConnector(
            "throwing_availability",
            Identity("Acme", "Two", "SERIAL-2"),
            ConnectorCapability.Identity)
        {
            ThrowAvailability = true
        };
        var throwingRead = new TestConnector(
            "throwing_read",
            Identity("Acme", "Three", "SERIAL-3"),
            ConnectorCapability.Identity)
        {
            ThrowIdentity = true
        };
        var healthy = new TestConnector(
            "healthy",
            Identity("Acme", "Four", "SERIAL-4"),
            ConnectorCapability.Identity);
        var logger = new ListLogger<ScannerConnectorManager>();
        var manager = CreateManager(logger, unavailable, throwingAvailability, throwingRead, healthy);

        var collection = await manager.CollectAsync(CancellationToken.None);

        Assert.Equal(2, collection.Scanners.Length);
        Assert.Contains(
            collection.Diagnostics,
            diagnostic => diagnostic.ConnectorId == "unavailable" &&
                diagnostic.State == ConnectorResultState.Unavailable &&
                diagnostic.ErrorCode == "wia_runtime_unavailable");
        Assert.Contains(
            collection.Diagnostics,
            diagnostic => diagnostic.ConnectorId == "throwing_availability" &&
                diagnostic.ErrorCode == ConnectorErrorCodes.AvailabilityCheckFailed);
        Assert.Contains(
            collection.Diagnostics,
            diagnostic => diagnostic.ConnectorId == "throwing_read" &&
                diagnostic.Operation == "identity" &&
                diagnostic.ErrorCode == ConnectorErrorCodes.ReadFailed);
        Assert.Contains(collection.Scanners, scanner =>
            scanner.Provenance.Any(descriptor => descriptor.ConnectorId == "healthy"));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("SECRET-SERIAL", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Collect_MergesOnlyStrongIdentityMatchesAndPreservesProvenance()
    {
        var manager = CreateManager(
            new TestConnector("wia", Identity("Acme", "Same Model", "SERIAL-1"), ConnectorCapability.Identity),
            new TestConnector("twain", Identity("Acme", "Same Model", "SERIAL-1"), ConnectorCapability.Identity),
            new TestConnector("isis", Identity("Other", "Same Model", "SERIAL-1"), ConnectorCapability.Identity),
            new TestConnector("source_a", Identity("Acme", "Same Model", null), ConnectorCapability.Identity),
            new TestConnector("source_b", Identity("Acme", "Same Model", null), ConnectorCapability.Identity));

        var collection = await manager.CollectAsync(CancellationToken.None);

        Assert.Equal(4, collection.Scanners.Length);
        var merged = Assert.Single(collection.Scanners.Where(scanner => scanner.Observations.Length == 2));
        Assert.Equal(2, merged.Provenance.Length);
        Assert.Contains(merged.Provenance, descriptor => descriptor.Protocol == "wia");
        Assert.Contains(merged.Provenance, descriptor => descriptor.Protocol == "twain");
        Assert.DoesNotContain("SERIAL-1", merged.ScannerId, StringComparison.Ordinal);
        Assert.All(collection.Scanners, scanner => Assert.StartsWith("scanner-", scanner.ScannerId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Collect_ReturnsImmutableCollectionSnapshot()
    {
        var manager = CreateManager(
            new TestConnector("mock", Identity("Acme", "ScanPro", "SERIAL-1"), ConnectorCapability.Identity));

        var collection = await manager.CollectAsync(CancellationToken.None);
        var changed = collection.Scanners.Add(collection.Scanners[0]);

        Assert.Single(collection.Scanners);
        Assert.Equal(2, changed.Length);
        Assert.Single(collection.Scanners[0].Observations);
        Assert.Single(collection.Scanners[0].Provenance);
    }

    [Fact]
    public void ConnectorValue_NormalizesUnstableErrorCodes()
    {
        var failed = ConnectorValue<ScannerIdentity>.Failed("Raw platform exception: SECRET-SERIAL");

        Assert.Equal(ConnectorResultState.Failed, failed.State);
        Assert.Equal(ConnectorErrorCodes.ReadFailed, failed.ErrorCode);
        Assert.Null(failed.Value);
    }

    private static ScannerConnectorManager CreateManager(params IScannerConnector[] connectors) =>
        CreateManager(NullLogger<ScannerConnectorManager>.Instance, connectors);

    private static ScannerConnectorManager CreateManager(
        ILogger<ScannerConnectorManager> logger,
        params IScannerConnector[] connectors) =>
        new(connectors, new ManualTimeProvider(DateTimeOffset.UtcNow), logger);

    private static ScannerIdentity Identity(string manufacturer, string model, string? serial) =>
        new(
            ConnectorValue<string>.Known(manufacturer),
            ConnectorValue<string>.Known(model),
            string.IsNullOrWhiteSpace(serial)
                ? ConnectorValue<string>.Unknown()
                : ConnectorValue<string>.Known(serial),
            ConnectorValue<string>.Known("USB"),
            ConnectorValue<string>.Unknown(),
            ConnectorValue<string>.Unknown());

    private sealed class TestConnector : ScannerConnectorBase
    {
        private readonly ConnectorValue<ScannerIdentity> _identity;

        public TestConnector(
            string connectorId,
            ScannerIdentity identity,
            params ConnectorCapability[] capabilities)
        {
            _identity = ConnectorValue<ScannerIdentity>.Known(identity);
            Descriptor = new ConnectorDescriptor(
                connectorId,
                connectorId,
                connectorId,
                null,
                false,
                capabilities.Prepend(ConnectorCapability.Discovery).Distinct().ToImmutableArray());
        }

        public override ConnectorDescriptor Descriptor { get; }

        public ConnectorAvailability Availability { get; init; } = ConnectorAvailability.Available();

        public ConnectorValue<ConnectorScannerHealth> HealthResult { get; init; } =
            ConnectorValue<ConnectorScannerHealth>.Unknown();

        public bool ThrowAvailability { get; init; }

        public bool ThrowIdentity { get; init; }

        public int IdentityCalls { get; private set; }

        public int FirmwareCalls { get; private set; }

        public int HealthCalls { get; private set; }

        public override Task<ConnectorAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken) =>
            ThrowAvailability
                ? throw new InvalidOperationException("SECRET-SERIAL must never be logged")
                : Task.FromResult(Availability);

        public override Task<ConnectorValue<ImmutableArray<ScannerConnectionTarget>>> DiscoverAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(ConnectorValue<ImmutableArray<ScannerConnectionTarget>>.Known(
                [new ScannerConnectionTarget($"target-{Descriptor.ConnectorId}", Descriptor.ConnectorId)]));

        public override Task<ConnectorValue<ScannerIdentity>> ReadIdentityAsync(
            ScannerConnectionTarget target,
            CancellationToken cancellationToken)
        {
            IdentityCalls++;
            return ThrowIdentity
                ? throw new InvalidOperationException("SECRET-SERIAL must never be logged")
                : Task.FromResult(_identity);
        }

        public override Task<ConnectorValue<ScannerFirmware>> ReadFirmwareAsync(
            ScannerConnectionTarget target,
            CancellationToken cancellationToken)
        {
            FirmwareCalls++;
            return Task.FromResult(ConnectorValue<ScannerFirmware>.Unknown());
        }

        public override Task<ConnectorValue<ConnectorScannerHealth>> ReadHealthAsync(
            ScannerConnectionTarget target,
            CancellationToken cancellationToken)
        {
            HealthCalls++;
            return Task.FromResult(HealthResult);
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
