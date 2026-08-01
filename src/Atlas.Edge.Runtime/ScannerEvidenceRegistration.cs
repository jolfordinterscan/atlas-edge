using Atlas.Edge.Configuration;
using Atlas.Edge.ScannerDiscovery;
using Atlas.Edge.ScannerEvidence;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Atlas.Edge.Runtime;

public static class ScannerEvidenceRegistration
{
    public static bool AddScannerEvidenceStartup(
        this IServiceCollection services,
        AtlasEdgeOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.ScannerEvidenceEnabled)
        {
            return false;
        }

        var developmentMock = string.Equals(
                options.ScannerEvidenceMode,
                AtlasEdgeOptions.ScannerEvidenceModeMock,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(options.EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase);
        if (developmentMock)
        {
            services.AddSingleton<IScannerEvidenceProvider, DevelopmentMockEvidenceProvider>();
        }
        else if (string.Equals(
            options.ScannerEvidenceMode,
            AtlasEdgeOptions.ScannerEvidenceModePlatform,
            StringComparison.OrdinalIgnoreCase))
        {
            AddPlatformProviders(services, options);
        }
        else
        {
            return false;
        }

        services.TryAddSingleton<ScannerEvidenceState>();
        services.TryAddSingleton<IScannerEvidenceManager, ScannerEvidenceManager>();
        services.AddHostedService<ScannerEvidenceHostedService>();
        return true;
    }

    private static void AddPlatformProviders(IServiceCollection services, AtlasEdgeOptions options)
    {
        var providers = options.ScannerEvidenceProviders ?? [];
        if (Contains(providers, "Wia"))
        {
            services.TryAddSingleton<IWiaScannerSourceCatalog, WiaScannerSourceCatalog>();
            services.AddSingleton<IScannerEvidenceProvider, WiaScannerEvidenceProvider>();
        }

        if (Contains(providers, "Twain"))
        {
            services.TryAddSingleton<ITwainScannerSourceCatalog, TwainScannerSourceCatalog>();
            services.AddSingleton<IScannerEvidenceProvider, TwainScannerEvidenceProvider>();
        }

        if (Contains(providers, "Isis"))
        {
            services.TryAddSingleton<IIsisScannerSourceCatalog, IsisScannerSourceCatalog>();
            services.AddSingleton<IScannerEvidenceProvider, IsisScannerEvidenceProvider>();
        }

        if (providers.Any(provider => provider.StartsWith("Windows", StringComparison.OrdinalIgnoreCase)) ||
            Contains(providers, "Registry"))
        {
            AddWindowsCatalogs(services);
        }

        if (Contains(providers, "WindowsPnp"))
        {
            services.AddSingleton<IScannerEvidenceProvider, WindowsPnpEvidenceProvider>();
        }

        if (Contains(providers, "WindowsDriver"))
        {
            services.AddSingleton<IScannerEvidenceProvider, WindowsDriverEvidenceProvider>();
        }

        if (Contains(providers, "WindowsService"))
        {
            services.AddSingleton<IScannerEvidenceProvider>(provider => new WindowsServiceEvidenceProvider(
                provider.GetRequiredService<IWindowsServiceEvidenceCatalog>(),
                provider.GetRequiredService<IPlatformContext>(),
                options.ScannerEvidenceServiceNames ?? []));
        }

        if (Contains(providers, "WindowsEventLog"))
        {
            services.AddSingleton<IScannerEvidenceProvider>(provider => new WindowsEventLogEvidenceProvider(
                provider.GetRequiredService<IWindowsEventEvidenceCatalog>(),
                provider.GetRequiredService<IPlatformContext>(),
                options.ScannerEvidenceEventLogChannels ?? [],
                options.ScannerEvidenceEventLogProviders ?? []));
        }

        if (Contains(providers, "Registry"))
        {
            services.AddSingleton<IScannerEvidenceProvider>(provider => new WindowsRegistryEvidenceProvider(
                provider.GetRequiredService<IWindowsRegistryEvidenceCatalog>(),
                provider.GetRequiredService<IPlatformContext>(),
                options.ScannerEvidenceRegistryPaths ?? []));
        }

        if (Contains(providers, "LocalLog"))
        {
            services.AddSingleton<IScannerEvidenceProvider>(_ => new AllowlistedLocalLogEvidenceProvider(
                options.ScannerEvidenceLogDirectories ?? [],
                (options.ScannerEvidenceLogFiles ?? []).Select(path => new LocalLogTarget(path)),
                options.ScannerEvidenceMaximumFileSizeBytes,
                options.ScannerEvidenceMaximumReadBytes));
        }

        if (Contains(providers, "Network"))
        {
            services.TryAddSingleton<INetworkEvidenceReader, UnavailableNetworkEvidenceReader>();
            services.AddSingleton<IScannerEvidenceProvider>(provider => new ConfiguredNetworkEvidenceProvider(
                provider.GetRequiredService<INetworkEvidenceReader>(),
                (options.ScannerEvidenceNetworkTargets ?? []).Select(target =>
                    new ConfiguredNetworkEvidenceTarget(new Uri(target, UriKind.Absolute))),
                options.ScannerEvidenceSnmpEnabled));
        }
    }

    private static void AddWindowsCatalogs(IServiceCollection services)
    {
        services.TryAddSingleton<IPlatformContext, SystemPlatformContext>();
        services.TryAddSingleton<UnavailableWindowsEvidenceCatalog>();
        services.TryAddSingleton<IWindowsPnpEvidenceCatalog>(provider =>
            provider.GetRequiredService<UnavailableWindowsEvidenceCatalog>());
        services.TryAddSingleton<IWindowsDriverEvidenceCatalog>(provider =>
            provider.GetRequiredService<UnavailableWindowsEvidenceCatalog>());
        services.TryAddSingleton<IWindowsServiceEvidenceCatalog>(provider =>
            provider.GetRequiredService<UnavailableWindowsEvidenceCatalog>());
        services.TryAddSingleton<IWindowsEventEvidenceCatalog>(provider =>
            provider.GetRequiredService<UnavailableWindowsEvidenceCatalog>());
        services.TryAddSingleton<IWindowsRegistryEvidenceCatalog>(provider =>
            provider.GetRequiredService<UnavailableWindowsEvidenceCatalog>());
    }

    private static bool Contains(IEnumerable<string> values, string expected) =>
        values.Contains(expected, StringComparer.OrdinalIgnoreCase);
}
