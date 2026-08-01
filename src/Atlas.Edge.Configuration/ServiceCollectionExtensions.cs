using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Atlas.Edge.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAtlasEdgeConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<AtlasEdgeOptions>()
            .Bind(configuration.GetSection(AtlasEdgeOptions.SectionName))
            .ValidateDataAnnotations()
            .Services
            .AddSingleton<IValidateOptions<AtlasEdgeOptions>, AtlasEdgeOptionsValidator>();

        return services;
    }
}