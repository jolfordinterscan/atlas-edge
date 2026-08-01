using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Atlas.Edge.ScannerConnectors;

public interface IScannerConnectorManager
{
    Task<ScannerConnectorCollectionSnapshot> CollectAsync(CancellationToken cancellationToken);
}

public sealed class ScannerConnectorManager : IScannerConnectorManager
{
    private readonly ImmutableArray<IScannerConnector> _connectors;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ScannerConnectorManager> _logger;

    public ScannerConnectorManager(
        IEnumerable<IScannerConnector> connectors,
        TimeProvider timeProvider,
        ILogger<ScannerConnectorManager> logger)
    {
        _connectors = connectors.ToImmutableArray();
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ScannerConnectorCollectionSnapshot> CollectAsync(
        CancellationToken cancellationToken)
    {
        var observations = ImmutableArray.CreateBuilder<ScannerConnectorObservation>();
        var diagnostics = ImmutableArray.CreateBuilder<ConnectorDiagnostic>();

        foreach (var connector in _connectors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var availability = await CheckAvailabilityAsync(connector, cancellationToken);
            diagnostics.Add(new ConnectorDiagnostic(
                connector.Descriptor.ConnectorId,
                "availability",
                availability.State,
                availability.ErrorCode));

            if (availability.State != ConnectorResultState.Known)
            {
                continue;
            }

            var discovery = await InvokeAsync(
                connector,
                ConnectorCapability.Discovery,
                "discovery",
                () => connector.DiscoverAsync(cancellationToken),
                ConnectorErrorCodes.DiscoveryFailed,
                cancellationToken);
            diagnostics.Add(ToDiagnostic(connector, "discovery", discovery));
            if (discovery.State != ConnectorResultState.Known)
            {
                continue;
            }

            foreach (var target in discovery.Value)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var observation = await ReadObservationAsync(
                    connector,
                    target,
                    diagnostics,
                    cancellationToken);
                observations.Add(observation);
            }
        }

        var snapshots = observations
            .GroupBy(CreateMergeKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => CreateSnapshot(group.Key, group))
            .OrderBy(snapshot => snapshot.ScannerId, StringComparer.Ordinal)
            .ToImmutableArray();

        return new ScannerConnectorCollectionSnapshot(
            _timeProvider.GetUtcNow(),
            snapshots,
            diagnostics.ToImmutable());
    }

    private async Task<ScannerConnectorObservation> ReadObservationAsync(
        IScannerConnector connector,
        ScannerConnectionTarget target,
        ImmutableArray<ConnectorDiagnostic>.Builder diagnostics,
        CancellationToken cancellationToken)
    {
        var identity = await ReadAsync(
            connector,
            target,
            ConnectorCapability.Identity,
            "identity",
            connector.ReadIdentityAsync,
            diagnostics,
            cancellationToken);
        var capabilities = await ReadAsync(
            connector,
            target,
            ConnectorCapability.Capabilities,
            "capabilities",
            connector.ReadCapabilitiesAsync,
            diagnostics,
            cancellationToken);
        var firmware = await ReadAsync(
            connector,
            target,
            ConnectorCapability.Firmware,
            "firmware",
            connector.ReadFirmwareAsync,
            diagnostics,
            cancellationToken);
        var counters = await ReadAsync(
            connector,
            target,
            ConnectorCapability.Counters,
            "counters",
            connector.ReadCountersAsync,
            diagnostics,
            cancellationToken);
        var health = await ReadAsync(
            connector,
            target,
            ConnectorCapability.Health,
            "health",
            connector.ReadHealthAsync,
            diagnostics,
            cancellationToken);
        var status = await ReadAsync(
            connector,
            target,
            ConnectorCapability.CurrentStatus,
            "current_status",
            connector.ReadCurrentStatusAsync,
            diagnostics,
            cancellationToken);
        var connectorDiagnostics = await ReadAsync(
            connector,
            target,
            ConnectorCapability.Diagnostics,
            "diagnostics",
            connector.ReadDiagnosticsAsync,
            diagnostics,
            cancellationToken);
        var logs = await ReadAsync(
            connector,
            target,
            ConnectorCapability.LogReferences,
            "log_references",
            connector.ReadLogReferencesAsync,
            diagnostics,
            cancellationToken);

        return new ScannerConnectorObservation(
            connector.Descriptor,
            target,
            identity,
            capabilities,
            firmware,
            counters,
            health,
            status,
            connectorDiagnostics,
            logs);
    }

    private async Task<ConnectorValue<T>> ReadAsync<T>(
        IScannerConnector connector,
        ScannerConnectionTarget target,
        ConnectorCapability capability,
        string operation,
        Func<ScannerConnectionTarget, CancellationToken, Task<ConnectorValue<T>>> read,
        ImmutableArray<ConnectorDiagnostic>.Builder diagnostics,
        CancellationToken cancellationToken)
    {
        var result = await InvokeAsync(
            connector,
            capability,
            operation,
            () => read(target, cancellationToken),
            ConnectorErrorCodes.ReadFailed,
            cancellationToken);
        diagnostics.Add(ToDiagnostic(connector, operation, result));
        return result;
    }

    private async Task<ConnectorAvailability> CheckAvailabilityAsync(
        IScannerConnector connector,
        CancellationToken cancellationToken)
    {
        try
        {
            return await connector.CheckAvailabilityAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            LogFailure(connector.Descriptor.ConnectorId, "availability", ConnectorErrorCodes.AvailabilityCheckFailed);
            return ConnectorAvailability.Failed();
        }
    }

    private async Task<ConnectorValue<T>> InvokeAsync<T>(
        IScannerConnector connector,
        ConnectorCapability capability,
        string operation,
        Func<Task<ConnectorValue<T>>> invoke,
        string failureCode,
        CancellationToken cancellationToken)
    {
        if (!connector.Descriptor.Capabilities.Contains(capability))
        {
            return ConnectorValue<T>.Unsupported();
        }

        try
        {
            return await invoke();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            LogFailure(connector.Descriptor.ConnectorId, operation, failureCode);
            return ConnectorValue<T>.Failed(failureCode);
        }
    }

    private void LogFailure(string connectorId, string operation, string errorCode) =>
        _logger.LogWarning(
            "Scanner connector {ConnectorId} operation {Operation} failed with status {ErrorCode}; other connectors will continue.",
            connectorId,
            operation,
            errorCode);

    private static ConnectorDiagnostic ToDiagnostic<T>(
        IScannerConnector connector,
        string operation,
        ConnectorValue<T> result) =>
        new(connector.Descriptor.ConnectorId, operation, result.State, result.ErrorCode);

    private static string CreateMergeKey(ScannerConnectorObservation observation)
    {
        if (observation.Identity is
            {
                State: ConnectorResultState.Known,
                Value.Manufacturer.State: ConnectorResultState.Known,
                Value.SerialNumber.State: ConnectorResultState.Known
            })
        {
            return $"serial|{Normalize(observation.Identity.Value.Manufacturer.Value)}|" +
                Normalize(observation.Identity.Value.SerialNumber.Value);
        }

        return $"target|{Normalize(observation.Connector.ConnectorId)}|{Normalize(observation.Target.TargetId)}";
    }

    private static ScannerConnectorSnapshot CreateSnapshot(
        string mergeKey,
        IEnumerable<ScannerConnectorObservation> sourceObservations)
    {
        var observations = sourceObservations.ToImmutableArray();
        return new ScannerConnectorSnapshot(
            CreateScannerId(mergeKey),
            Merge(observations.Select(observation => observation.Identity)),
            Merge(observations.Select(observation => observation.Capabilities)),
            Merge(observations.Select(observation => observation.Firmware)),
            Merge(observations.Select(observation => observation.Counters)),
            Merge(observations.Select(observation => observation.Health)),
            Merge(observations.Select(observation => observation.Status)),
            Merge(observations.Select(observation => observation.Diagnostics)),
            Merge(observations.Select(observation => observation.LogReferences)),
            observations,
            observations.Select(observation => observation.Connector).Distinct().ToImmutableArray());
    }

    private static ConnectorValue<T> Merge<T>(IEnumerable<ConnectorValue<T>> sourceValues)
    {
        var values = sourceValues.ToImmutableArray();
        var known = values.FirstOrDefault(value => value.State == ConnectorResultState.Known);
        if (known is not null)
        {
            return known;
        }

        var failed = values.FirstOrDefault(value => value.State == ConnectorResultState.Failed);
        if (failed is not null)
        {
            return failed;
        }

        var unavailable = values.FirstOrDefault(value => value.State == ConnectorResultState.Unavailable);
        if (unavailable is not null)
        {
            return unavailable;
        }

        var unknown = values.FirstOrDefault(value => value.State == ConnectorResultState.Unknown);
        return unknown ?? ConnectorValue<T>.Unsupported();
    }

    private static string CreateScannerId(string mergeKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(mergeKey));
        return $"scanner-{Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant()}";
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim().ToUpperInvariant();
}
