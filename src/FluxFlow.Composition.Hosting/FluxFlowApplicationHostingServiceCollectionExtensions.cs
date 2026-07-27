using FluxFlow.Composition.Model;
using FluxFlow.Engine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FluxFlow.Composition.Hosting;

[Obsolete("Use FluxFlowApplicationServiceCollectionExtensions.AddFluxFlow from FluxFlow.Engine.")]
public static class FluxFlowApplicationHostingServiceCollectionExtensions
{
    public static IServiceCollection AddFluxFlowApplication(
        this IServiceCollection services,
        ApplicationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(definition);
        FluxFlowApplicationServiceCollectionExtensions.AddFluxFlow(services, definition);
        return AddCompatibilityServices(services);
    }

    public static IServiceCollection AddFluxFlowApplication(
        this IServiceCollection services,
        IConfiguration configuration,
        string? sectionName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        FluxFlowApplicationServiceCollectionExtensions.AddFluxFlow(
            services,
            configuration,
            sectionName);
        return AddCompatibilityServices(services);
    }

    public static IServiceCollection AddFluxFlowApplication(
        this IServiceCollection services,
        IApplicationDefinitionSource definitionSource)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(definitionSource);
        FluxFlowApplicationServiceCollectionExtensions.AddFluxFlow(services, definitionSource);
        return AddCompatibilityServices(services);
    }

    public static IServiceCollection ConfigureFluxFlowApplication(
        this IServiceCollection services,
        Action<ApplicationRevisionHostingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        services.Configure(configure);
        return services;
    }

    private static IServiceCollection AddCompatibilityServices(IServiceCollection services)
    {
        services.AddOptions<ApplicationRevisionHostingOptions>();
        services.AddOptions<FluxFlowApplicationOptions>()
            .Configure<IOptions<ApplicationRevisionHostingOptions>>(static (current, legacy) =>
            {
                current.InitialRevisionId = legacy.Value.InitialRevisionId;
                current.StartWithHost = legacy.Value.StartApplicationWithHost;
                current.StopWithHost = legacy.Value.StopApplicationWithHost;
            });
        services.TryAddSingleton<ApplicationRevisionHost>();
        services.TryAddSingleton<IApplicationRevisionHost>(static provider =>
            provider.GetRequiredService<ApplicationRevisionHost>());
        return services;
    }
}
