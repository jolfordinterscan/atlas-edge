using System.Collections.Immutable;
using Microsoft.Extensions.Logging;

namespace Atlas.Edge.ScannerEvidence;

public interface IScannerEvidenceManager
{
    Task<ScannerEvidenceCollectionSnapshot> CollectAsync(CancellationToken cancellationToken);
}

public sealed class ScannerEvidenceManager : IScannerEvidenceManager
{
    private readonly ImmutableArray<IScannerEvidenceProvider> _providers;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ScannerEvidenceManager> _logger;

    public ScannerEvidenceManager(
        IEnumerable<IScannerEvidenceProvider> providers,
        TimeProvider timeProvider,
        ILogger<ScannerEvidenceManager> logger)
    {
        _providers = providers.ToImmutableArray();
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ScannerEvidenceCollectionSnapshot> CollectAsync(CancellationToken cancellationToken)
    {
        var observations = ImmutableArray.CreateBuilder<ScannerEvidenceObservation>();
        var diagnostics = ImmutableArray.CreateBuilder<EvidenceProviderDiagnostic>();

        foreach (var provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var availability = await CheckAvailabilityAsync(provider, cancellationToken);
            diagnostics.Add(new EvidenceProviderDiagnostic(
                provider.Descriptor.ProviderId,
                "availability",
                availability.State,
                availability.ErrorCode));
            if (availability.State != EvidenceValueState.Known)
            {
                continue;
            }

            var targets = await InvokeAsync(
                provider,
                EvidenceCapability.Discovery,
                "discovery",
                () => provider.DiscoverTargetsAsync(cancellationToken),
                EvidenceErrorCodes.DiscoveryFailed,
                cancellationToken);
            diagnostics.Add(ToDiagnostic(provider, "discovery", targets));
            if (targets.State != EvidenceValueState.Known)
            {
                continue;
            }

            foreach (var target in targets.Value)
            {
                observations.Add(await ReadObservationAsync(provider, target, diagnostics, cancellationToken));
            }
        }

        var snapshots = Correlate(observations.ToImmutable())
            .Select(group => CreateSnapshot(group.CorrelationKey, group.Observations))
            .OrderBy(snapshot => snapshot.ScannerId, StringComparer.Ordinal)
            .ToImmutableArray();
        return new ScannerEvidenceCollectionSnapshot(
            _timeProvider.GetUtcNow(),
            snapshots,
            diagnostics.ToImmutable());
    }

    private async Task<ScannerEvidenceObservation> ReadObservationAsync(
        IScannerEvidenceProvider provider,
        ScannerEvidenceTarget target,
        ImmutableArray<EvidenceProviderDiagnostic>.Builder diagnostics,
        CancellationToken cancellationToken)
    {
        var identity = await ReadAsync(provider, target, EvidenceCapability.DeviceIdentity, "identity",
            provider.ReadIdentityAsync, diagnostics, cancellationToken);
        var driver = await ReadAsync(provider, target, EvidenceCapability.Driver, "driver",
            provider.ReadDriverAsync, diagnostics, cancellationToken);
        var connection = await ReadAsync(provider, target, EvidenceCapability.Connection, "connection",
            provider.ReadConnectionAsync, diagnostics, cancellationToken);
        var services = await ReadAsync(provider, target, EvidenceCapability.Services, "services",
            provider.ReadServicesAsync, diagnostics, cancellationToken);
        var events = await ReadAsync(provider, target, EvidenceCapability.Events, "events",
            provider.ReadEventsAsync, diagnostics, cancellationToken);
        var counters = await ReadAsync(provider, target, EvidenceCapability.Counters, "counters",
            provider.ReadCountersAsync, diagnostics, cancellationToken);
        var firmware = await ReadAsync(provider, target, EvidenceCapability.Firmware, "firmware",
            provider.ReadFirmwareAsync, diagnostics, cancellationToken);
        var maintenance = await ReadAsync(provider, target, EvidenceCapability.Maintenance, "maintenance",
            provider.ReadMaintenanceAsync, diagnostics, cancellationToken);
        var logs = await ReadAsync(provider, target, EvidenceCapability.LogReferences, "log_references",
            provider.ReadLogReferencesAsync, diagnostics, cancellationToken);
        var network = await ReadAsync(provider, target, EvidenceCapability.Network, "network",
            provider.ReadNetworkAsync, diagnostics, cancellationToken);

        return new ScannerEvidenceObservation(
            provider.Descriptor,
            target,
            _timeProvider.GetUtcNow(),
            new EvidenceProvenance(
                provider.Descriptor.ProviderId,
                provider.Descriptor.SourceType,
                provider.Descriptor.SourceQuality,
                target.TargetId),
            identity,
            driver,
            connection,
            services,
            events,
            counters,
            firmware,
            maintenance,
            logs,
            network);
    }

    private async Task<EvidenceValue<T>> ReadAsync<T>(
        IScannerEvidenceProvider provider,
        ScannerEvidenceTarget target,
        EvidenceCapability capability,
        string operation,
        Func<ScannerEvidenceTarget, CancellationToken, Task<EvidenceValue<T>>> read,
        ImmutableArray<EvidenceProviderDiagnostic>.Builder diagnostics,
        CancellationToken cancellationToken)
    {
        var result = await InvokeAsync(
            provider,
            capability,
            operation,
            () => read(target, cancellationToken),
            EvidenceErrorCodes.CollectionFailed,
            cancellationToken);
        diagnostics.Add(ToDiagnostic(provider, operation, result));
        return result;
    }

    private async Task<EvidenceAvailability> CheckAvailabilityAsync(
        IScannerEvidenceProvider provider,
        CancellationToken cancellationToken)
    {
        try
        {
            return await provider.CheckAvailabilityAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            LogFailure(provider.Descriptor.ProviderId, "availability", EvidenceErrorCodes.AvailabilityFailed);
            return EvidenceAvailability.Failed();
        }
    }

    private async Task<EvidenceValue<T>> InvokeAsync<T>(
        IScannerEvidenceProvider provider,
        EvidenceCapability capability,
        string operation,
        Func<Task<EvidenceValue<T>>> invoke,
        string failureCode,
        CancellationToken cancellationToken)
    {
        if (!provider.Descriptor.Capabilities.Contains(capability))
        {
            return EvidenceValue<T>.Unsupported();
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
            LogFailure(provider.Descriptor.ProviderId, operation, failureCode);
            return EvidenceValue<T>.Failed(failureCode);
        }
    }

    private void LogFailure(string providerId, string operation, string errorCode) =>
        _logger.LogWarning(
            "Scanner evidence provider {ProviderId} operation {Operation} failed with status {ErrorCode}; collection will continue.",
            providerId,
            operation,
            errorCode);

    private static EvidenceProviderDiagnostic ToDiagnostic<T>(
        IScannerEvidenceProvider provider,
        string operation,
        EvidenceValue<T> result) =>
        new(provider.Descriptor.ProviderId, operation, result.State, result.ErrorCode);

    private static ImmutableArray<CorrelatedObservations> Correlate(
        ImmutableArray<ScannerEvidenceObservation> observations)
    {
        var parents = Enumerable.Range(0, observations.Length).ToArray();
        var firstByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        var keysByObservation = new ImmutableArray<string>[observations.Length];
        for (var index = 0; index < observations.Length; index++)
        {
            var keys = GetCorrelationKeys(observations[index]);
            keysByObservation[index] = keys;
            foreach (var key in keys)
            {
                if (firstByKey.TryGetValue(key, out var existing))
                {
                    Union(parents, index, existing);
                }
                else
                {
                    firstByKey[key] = index;
                }
            }
        }

        return Enumerable.Range(0, observations.Length)
            .GroupBy(index => Find(parents, index))
            .Select(group =>
            {
                var indexes = group.ToImmutableArray();
                var correlationKey = indexes
                    .SelectMany(index => keysByObservation[index])
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .FirstOrDefault() ?? $"target|{observations[indexes[0]].Source.ProviderId}|" +
                        observations[indexes[0]].Target.TargetId;
                return new CorrelatedObservations(
                    correlationKey,
                    indexes.Select(index => observations[index]).ToImmutableArray());
            })
            .ToImmutableArray();
    }

    private static ImmutableArray<string> GetCorrelationKeys(ScannerEvidenceObservation observation)
    {
        var keys = observation.Target.CorrelationKeys
            .Select(key => $"strong|{key.Kind}|{key.ValueHash}")
            .ToHashSet(StringComparer.Ordinal);
        if (observation.Identity is { State: EvidenceValueState.Known })
        {
            var identity = observation.Identity.Value;
            if (identity.Manufacturer.State == EvidenceValueState.Known &&
                identity.SerialNumber.State == EvidenceValueState.Known)
            {
                keys.Add("strong|ManufacturerSerial|" + EvidenceIdentity.Hash(
                    "manufacturer_serial",
                    identity.Manufacturer.Value,
                    identity.SerialNumber.Value));
            }

            if (identity.HardwareInstanceId.State == EvidenceValueState.Known)
            {
                keys.Add("strong|HardwareInstance|" + EvidenceIdentity.Hash(
                    "hardware",
                    identity.HardwareInstanceId.Value));
            }
        }

        if (observation.Connection is { State: EvidenceValueState.Known } &&
            observation.Connection.Value.StableUsbPath.State == EvidenceValueState.Known)
        {
            keys.Add("strong|StableUsbPath|" + EvidenceIdentity.Hash(
                "usb_path",
                observation.Connection.Value.StableUsbPath.Value));
        }

        return keys.OrderBy(value => value, StringComparer.Ordinal).ToImmutableArray();
    }

    private static int Find(int[] parents, int index)
    {
        while (parents[index] != index)
        {
            parents[index] = parents[parents[index]];
            index = parents[index];
        }

        return index;
    }

    private static void Union(int[] parents, int left, int right)
    {
        var leftRoot = Find(parents, left);
        var rightRoot = Find(parents, right);
        if (leftRoot != rightRoot)
        {
            parents[rightRoot] = leftRoot;
        }
    }

    private static ScannerEvidenceSnapshot CreateSnapshot(
        string correlationKey,
        IEnumerable<ScannerEvidenceObservation> sourceObservations)
    {
        var observations = sourceObservations.ToImmutableArray();
        return new ScannerEvidenceSnapshot(
            "evidence-" + EvidenceIdentity.Hash("scanner_evidence", correlationKey),
            Merge(observations.Select(observation => observation.Identity)),
            Merge(observations.Select(observation => observation.Driver)),
            Merge(observations.Select(observation => observation.Connection)),
            Merge(observations.Select(observation => observation.Services)),
            Merge(observations.Select(observation => observation.Events)),
            Merge(observations.Select(observation => observation.Counters)),
            Merge(observations.Select(observation => observation.Firmware)),
            Merge(observations.Select(observation => observation.Maintenance)),
            Merge(observations.Select(observation => observation.LogReferences)),
            Merge(observations.Select(observation => observation.Network)),
            observations,
            observations.Select(observation => observation.Provenance).ToImmutableArray());
    }

    private static EvidenceValue<T> Merge<T>(IEnumerable<EvidenceValue<T>> source)
    {
        var values = source.ToImmutableArray();
        return values.FirstOrDefault(value => value.State == EvidenceValueState.Known) ??
            values.FirstOrDefault(value => value.State == EvidenceValueState.Failed) ??
            values.FirstOrDefault(value => value.State == EvidenceValueState.Unavailable) ??
            values.FirstOrDefault(value => value.State == EvidenceValueState.Unknown) ??
            EvidenceValue<T>.Unsupported();
    }

    private sealed record CorrelatedObservations(
        string CorrelationKey,
        ImmutableArray<ScannerEvidenceObservation> Observations);
}
