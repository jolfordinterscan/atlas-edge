using System.Collections.Immutable;
using System.Text;

namespace Atlas.Edge.ScannerEvidence;

public sealed record LocalLogTarget(
    string FilePath,
    string? AdministratorMappingId = null);

public sealed class AllowlistedLocalLogEvidenceProvider : ScannerEvidenceProviderBase
{
    private readonly ImmutableArray<string> _allowedDirectories;
    private readonly ImmutableDictionary<string, LocalLogTarget> _targets;
    private readonly long _maximumFileSizeBytes;
    private readonly int _maximumReadBytes;

    public AllowlistedLocalLogEvidenceProvider(
        IEnumerable<string> allowedDirectories,
        IEnumerable<LocalLogTarget> targets,
        long maximumFileSizeBytes,
        int maximumReadBytes)
    {
        if (maximumFileSizeBytes <= 0 || maximumReadBytes <= 0 || maximumReadBytes > maximumFileSizeBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumReadBytes));
        }

        _allowedDirectories = allowedDirectories.Select(Path.GetFullPath).Distinct().ToImmutableArray();
        if (_allowedDirectories.Any(directory => !EvidenceSafetyPolicy.IsSafeAllowlistedDirectory(directory)))
        {
            throw new ArgumentException("Every local log directory must be an explicit non-root absolute path.");
        }

        if (_allowedDirectories.Any(IsSymbolicLink))
        {
            throw new ArgumentException("Allowlisted local log directories must not be symbolic links.");
        }

        var targetBuilder = ImmutableDictionary.CreateBuilder<string, LocalLogTarget>(StringComparer.Ordinal);
        foreach (var target in targets)
        {
            if (!EvidenceSafetyPolicy.IsSafeAllowlistedFile(target.FilePath, _allowedDirectories))
            {
                throw new ArgumentException("Every local log file must be inside an allowlisted directory.");
            }

            targetBuilder["log-" + EvidenceIdentity.Hash("local_log", target.FilePath)] =
                target with { FilePath = Path.GetFullPath(target.FilePath) };
        }

        _targets = targetBuilder.ToImmutable();
        _maximumFileSizeBytes = maximumFileSizeBytes;
        _maximumReadBytes = maximumReadBytes;
    }

    public override EvidenceSourceDescriptor Descriptor { get; } = new(
        "local_log",
        "Allowlisted Local Log Evidence",
        "LocalFile",
        EvidenceSourceQuality.LocalLog,
        false,
        [EvidenceCapability.Discovery, EvidenceCapability.LogReferences]);

    public override Task<EvidenceAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(EvidenceAvailability.Available());
    }

    public override Task<EvidenceValue<ImmutableArray<ScannerEvidenceTarget>>> DiscoverTargetsAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(EvidenceValue<ImmutableArray<ScannerEvidenceTarget>>.Known(
            _targets.Select(item =>
            {
                var correlations = ImmutableArray.CreateBuilder<EvidenceCorrelationKey>();
                Correlation.Add(
                    correlations,
                    EvidenceCorrelationKind.AdministratorMapping,
                    "administrator_mapping",
                    item.Value.AdministratorMappingId);
                return new ScannerEvidenceTarget(item.Key, Descriptor.ProviderId, correlations.ToImmutable());
            }).ToImmutableArray()));
    }

    public override async Task<EvidenceValue<ImmutableArray<LogEvidenceReference>>> ReadLogReferencesAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken)
    {
        if (!_targets.TryGetValue(target.TargetId, out var configuredTarget))
        {
            return EvidenceValue<ImmutableArray<LogEvidenceReference>>.Failed(EvidenceErrorCodes.TargetNotFound);
        }

        if (ContainsSymbolicLink(configuredTarget.FilePath))
        {
            return EvidenceValue<ImmutableArray<LogEvidenceReference>>.Failed(EvidenceErrorCodes.SymbolicLinkNotAllowed);
        }

        try
        {
            var file = new FileInfo(configuredTarget.FilePath);
            if (!file.Exists)
            {
                return EvidenceValue<ImmutableArray<LogEvidenceReference>>.Known(
                    [new LogEvidenceReference(
                        target.TargetId,
                        EvidenceValue<bool>.Known(false),
                        EvidenceValue<DateTimeOffset>.Unknown(),
                        EvidenceValue<long>.Unknown(),
                        ImmutableArray<string>.Empty)]);
            }

            if (file.Length > _maximumFileSizeBytes)
            {
                return EvidenceValue<ImmutableArray<LogEvidenceReference>>.Failed(EvidenceErrorCodes.FileTooLarge);
            }

            var stableCodes = await ExtractStableCodesAsync(file.FullName, cancellationToken);
            return EvidenceValue<ImmutableArray<LogEvidenceReference>>.Known(
                [new LogEvidenceReference(
                    target.TargetId,
                    EvidenceValue<bool>.Known(true),
                    EvidenceValue<DateTimeOffset>.Known(file.LastWriteTimeUtc),
                    EvidenceValue<long>.Known(file.Length),
                    stableCodes)]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (FileNotFoundException)
        {
            return MissingReference(target.TargetId);
        }
        catch (DirectoryNotFoundException)
        {
            return MissingReference(target.TargetId);
        }
        catch (IOException)
        {
            return EvidenceValue<ImmutableArray<LogEvidenceReference>>.Failed();
        }
        catch (UnauthorizedAccessException)
        {
            return EvidenceValue<ImmutableArray<LogEvidenceReference>>.Failed();
        }
    }

    private async Task<ImmutableArray<string>> ExtractStableCodesAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            useAsync: true);
        var length = (int)Math.Min(stream.Length, _maximumReadBytes);
        var buffer = new byte[length];
        var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, length), cancellationToken);
        var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
        return text.Split('\n')
            .Select(ExtractStableCode)
            .Where(code => code is not null)
            .Select(code => code!)
            .Distinct(StringComparer.Ordinal)
            .Take(64)
            .ToImmutableArray();
    }

    private static string? ExtractStableCode(string line)
    {
        const string marker = "error_code=";
        var index = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var candidate = line[(index + marker.Length)..]
            .Trim()
            .Split(' ', '\t', '\r', ';', ',')[0]
            .ToLowerInvariant();
        return candidate.Length is > 0 and <= 64 && candidate.All(character =>
            char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '_')
                ? candidate
                : null;
    }

    private bool ContainsSymbolicLink(string filePath)
    {
        var allowedRoot = _allowedDirectories.First(directory => EvidenceSafetyPolicy.IsWithin(filePath, directory));
        if (IsSymbolicLink(allowedRoot))
        {
            return true;
        }

        var relative = Path.GetRelativePath(allowedRoot, filePath);
        var current = allowedRoot;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current))
            {
                return false;
            }

            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSymbolicLink(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return false;
        }

        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static EvidenceValue<ImmutableArray<LogEvidenceReference>> MissingReference(string targetId) =>
        EvidenceValue<ImmutableArray<LogEvidenceReference>>.Known(
            [new LogEvidenceReference(
                targetId,
                EvidenceValue<bool>.Known(false),
                EvidenceValue<DateTimeOffset>.Unknown(),
                EvidenceValue<long>.Unknown(),
                ImmutableArray<string>.Empty)]);
}

public sealed record ConfiguredNetworkEvidenceTarget(
    Uri Endpoint,
    string? AdministratorMappingId = null);

public interface INetworkEvidenceReader
{
    Task<EvidenceValue<NetworkEvidence>> ReadAsync(
        ConfiguredNetworkEvidenceTarget target,
        CancellationToken cancellationToken);
}

public sealed class UnavailableNetworkEvidenceReader : INetworkEvidenceReader
{
    public Task<EvidenceValue<NetworkEvidence>> ReadAsync(
        ConfiguredNetworkEvidenceTarget target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(EvidenceValue<NetworkEvidence>.Unavailable("network_reader_unavailable"));
    }
}

public sealed class ConfiguredNetworkEvidenceProvider : ScannerEvidenceProviderBase
{
    private readonly INetworkEvidenceReader _reader;
    private readonly ImmutableDictionary<string, ConfiguredNetworkEvidenceTarget> _targets;

    public ConfiguredNetworkEvidenceProvider(
        INetworkEvidenceReader reader,
        IEnumerable<ConfiguredNetworkEvidenceTarget> targets,
        bool snmpEnabled = false)
    {
        _reader = reader;
        var configuredTargets = targets.ToImmutableArray();
        if (configuredTargets.Any(target => !EvidenceSafetyPolicy.IsSafeNetworkTarget(
            target.Endpoint.AbsoluteUri,
            allowLocalDevelopmentHttp: false,
            snmpEnabled)))
        {
            throw new ArgumentException("Network evidence targets must use HTTPS or explicitly enabled SNMP.");
        }

        _targets = configuredTargets.ToImmutableDictionary(
            target => "network-" + EvidenceIdentity.Hash("network_target", target.Endpoint.AbsoluteUri),
            StringComparer.Ordinal);
    }

    public override EvidenceSourceDescriptor Descriptor { get; } = new(
        "configured_network",
        "Configured Read-only Network Evidence",
        "Network",
        EvidenceSourceQuality.NetworkInterface,
        false,
        [EvidenceCapability.Discovery, EvidenceCapability.Network]);

    public override Task<EvidenceAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(EvidenceAvailability.Available());
    }

    public override Task<EvidenceValue<ImmutableArray<ScannerEvidenceTarget>>> DiscoverTargetsAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(EvidenceValue<ImmutableArray<ScannerEvidenceTarget>>.Known(_targets.Select(item =>
        {
            var correlations = ImmutableArray.CreateBuilder<EvidenceCorrelationKey>();
            Correlation.Add(
                correlations,
                EvidenceCorrelationKind.AdministratorMapping,
                "administrator_mapping",
                item.Value.AdministratorMappingId);
            return new ScannerEvidenceTarget(item.Key, Descriptor.ProviderId, correlations.ToImmutable());
        }).ToImmutableArray()));
    }

    public override Task<EvidenceValue<NetworkEvidence>> ReadNetworkAsync(
        ScannerEvidenceTarget target,
        CancellationToken cancellationToken) =>
        _targets.TryGetValue(target.TargetId, out var configuredTarget)
            ? _reader.ReadAsync(configuredTarget, cancellationToken)
            : Task.FromResult(EvidenceValue<NetworkEvidence>.Failed(EvidenceErrorCodes.TargetNotFound));
}
