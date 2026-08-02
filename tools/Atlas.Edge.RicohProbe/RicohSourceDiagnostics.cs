using System.Globalization;
using Atlas.Edge.ScannerDiscovery;

namespace Atlas.Edge.RicohProbe;

public sealed class WindowsRicohSourceEnvironmentCatalog : IRicohSourceEnvironmentCatalog
{
    public async Task<RicohSourceEnvironmentSnapshot> InspectAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return RicohSourceEnvironmentSnapshot.Empty;
        }

        var diagnostics = new List<string>();
        var sources = new List<RicohEnvironmentSource>();
        var wia = await ReadWiaAsync(sources, diagnostics, cancellationToken).ConfigureAwait(false);
        var pnp = await ReadPnpAsync(sources, diagnostics, cancellationToken).ConfigureAwait(false);
        var twain = await ReadTwainAsync(sources, diagnostics, cancellationToken).ConfigureAwait(false);
        return new RicohSourceEnvironmentSnapshot(
            wia,
            pnp,
            twain,
            sources
                .GroupBy(value => $"{value.Kind}|{value.Name}|{value.Architecture}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(value => value.Kind, StringComparer.Ordinal)
                .ThenBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            diagnostics.ToArray());
    }

    private static async Task<bool> ReadWiaAsync(
        ICollection<RicohEnvironmentSource> output,
        ICollection<string> diagnostics,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await new WiaScannerSourceCatalog().EnumerateAsync(cancellationToken).ConfigureAwait(false);
            foreach (var source in result.Sources)
            {
                output.Add(Create("WIA", source.Model, source.Manufacturer, source.Driver, "Unknown"));
            }

            return result.IsAvailable;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            diagnostics.Add("ricoh_source_wia_unavailable");
            return false;
        }
    }

    private static async Task<bool> ReadPnpAsync(
        ICollection<RicohEnvironmentSource> output,
        ICollection<string> diagnostics,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await new WindowsPnpScannerMetadataCatalog().ReadAsync(cancellationToken).ConfigureAwait(false);
            foreach (var source in result.Records)
            {
                output.Add(new RicohEnvironmentSource(
                    "WindowsPnP",
                    Value(source.FriendlyName),
                    Value(source.Manufacturer),
                    Value(source.DriverName),
                    source.DriverVersion,
                    source.DriverProvider,
                    "Unknown"));
            }

            return result.IsAvailable;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            diagnostics.Add("ricoh_source_pnp_unavailable");
            return false;
        }
    }

    private static async Task<bool> ReadTwainAsync(
        ICollection<RicohEnvironmentSource> output,
        ICollection<string> diagnostics,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await new TwainScannerSourceCatalog().EnumerateAsync(cancellationToken).ConfigureAwait(false);
            foreach (var source in result.Sources)
            {
                output.Add(Create(
                    "TWAIN",
                    source.Model,
                    source.Manufacturer,
                    source.Driver,
                    Architecture(source.SourceId)));
            }

            return result.IsAvailable;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            diagnostics.Add("ricoh_source_twain_unavailable");
            return false;
        }
    }

    private static RicohEnvironmentSource Create(
        string kind,
        string name,
        string manufacturer,
        ScannerDriver driver,
        string architecture) =>
        new(kind, name, manufacturer, driver.Name, driver.Version, driver.Provider, architecture);

    private static string Architecture(string sourceId)
    {
        if (sourceId.Contains("Registry32", StringComparison.OrdinalIgnoreCase) ||
            sourceId.Contains("twain_32", StringComparison.OrdinalIgnoreCase))
        {
            return "X86";
        }

        if (sourceId.Contains("Registry64", StringComparison.OrdinalIgnoreCase) ||
            sourceId.Contains("twain_64", StringComparison.OrdinalIgnoreCase))
        {
            return "X64";
        }

        return "Unknown";
    }

    private static string Value(string? value) => string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();
}

public static class RicohSourceDiagnosticBuilder
{
    public static IReadOnlyList<RicohSdkSourceDiagnostic> Build(
        RicohSdkSourceEnumeration enumeration,
        RicohSourceEnvironmentSnapshot environment) =>
        enumeration.Sources.Select(source => new RicohSdkSourceDiagnostic(
            source.Index,
            Sanitize(source.Name),
            source.Index == enumeration.SelectedIndexResult,
            "TwainDataSource",
            Associate(source.Name, environment.Sources),
            null,
            false)).ToArray();

    private static RicohSourceDriverAssociation? Associate(
        string sdkName,
        IReadOnlyList<RicohEnvironmentSource> environment)
    {
        var sdk = Normalize(sdkName);
        if (sdk.Length == 0)
        {
            return null;
        }

        var candidates = environment
            .Select(value => (Source: value, Name: Normalize(value.Name), Driver: Normalize(value.DriverName)))
            .Where(value => Matches(value.Name, sdk) || Matches(value.Driver, sdk))
            .OrderBy(value => value.Source.Kind == "TWAIN" ? 0 : value.Source.Kind == "WIA" ? 1 : 2)
            .ToArray();
        if (candidates.Length == 0)
        {
            return null;
        }

        var bestRank = candidates[0].Source.Kind == "TWAIN" ? 0 : candidates[0].Source.Kind == "WIA" ? 1 : 2;
        var best = candidates.Where(value =>
            (value.Source.Kind == "TWAIN" ? 0 : value.Source.Kind == "WIA" ? 1 : 2) == bestRank).ToArray();
        if (best.Length != 1)
        {
            return null;
        }

        var match = best[0].Source;
        return new RicohSourceDriverAssociation(
            match.Name,
            match.Kind,
            match.DriverName,
            match.DriverVersion,
            match.DriverProvider,
            match.Architecture,
            "UniqueNormalizedName");
    }

    private static string Normalize(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit)).ToUpper(CultureInfo.InvariantCulture);

    private static bool Matches(string candidate, string sdk) =>
        candidate.Length > 0 &&
        (candidate == sdk || candidate.Contains(sdk, StringComparison.Ordinal) ||
         sdk.Contains(candidate, StringComparison.Ordinal));

    private static string Sanitize(string value)
    {
        var sanitized = new string(value.Trim().Where(character => !char.IsControl(character)).Take(128).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "Unknown" : sanitized;
    }
}
