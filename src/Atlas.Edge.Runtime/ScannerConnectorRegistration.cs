using Atlas.Edge.Configuration;
using Atlas.Edge.ScannerConnectors;
using Atlas.Edge.ScannerDiscovery;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Atlas.Edge.Runtime;

public static class ScannerConnectorRegistration
{
    public static bool AddScannerConnectorStartup(
        this IServiceCollection services,
        bool enabled,
        string provider,
        string environmentName)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!enabled)
        {
            return false;
        }

        var useMock = string.Equals(
            provider,
            AtlasEdgeOptions.ScannerConnectorProviderMock,
            StringComparison.OrdinalIgnoreCase);
        var usePlatform = string.Equals(
            provider,
            AtlasEdgeOptions.ScannerConnectorProviderPlatform,
            StringComparison.OrdinalIgnoreCase);
        if ((!useMock && !usePlatform) ||
            (useMock && !string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        services.TryAddSingleton<ScannerConnectorState>();
        if (useMock)
        {
            services.AddSingleton<IScannerConnector, DevelopmentMockScannerConnector>();
        }
        else
        {
            services.TryAddSingleton<IWiaScannerSourceCatalog, WiaScannerSourceCatalog>();
            services.TryAddSingleton<ITwainScannerSourceCatalog, TwainScannerSourceCatalog>();
            services.TryAddSingleton<IIsisScannerSourceCatalog, IsisScannerSourceCatalog>();
            services.AddSingleton<IScannerConnector, WiaScannerConnector>();
            services.AddSingleton<IScannerConnector, TwainScannerConnector>();
            services.AddSingleton<IScannerConnector, IsisScannerConnector>();
        }

        services.TryAddSingleton<IScannerConnectorManager, ScannerConnectorManager>();
        services.AddHostedService<ScannerConnectorHostedService>();
        return true;
    }
}
