using FluxFlow.Composition;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FluxFlow.Composition.Hosting;

public static class FluxFlowCompositionHostingServiceCollectionExtensions
{
    public static CompositionHostingBuilder AddFluxFlowComposition(
        this IServiceCollection services,
        CompositionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(definition);

        return services.AddFluxFlowComposition(
            new StaticCompositionDefinitionSource(definition));
    }

    public static CompositionHostingBuilder AddFluxFlowComposition(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = CompositionConfigurationLoader.DefaultSectionName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return services.AddFluxFlowComposition(
            new ConfigurationCompositionDefinitionSource(configuration, sectionName));
    }

    public static CompositionHostingBuilder AddFluxFlowCompositionSection(
        this IServiceCollection services,
        IConfiguration configurationSection)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return services.AddFluxFlowComposition(
            new ConfigurationCompositionDefinitionSource(configurationSection, sectionName: ""));
    }

    public static CompositionHostingBuilder AddFluxFlowComposition(
        this IServiceCollection services,
        ICompositionDefinitionSource definitionSource)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(definitionSource);

        services.AddOptions<CompositionHostingOptions>();
        services.TryAddSingleton(definitionSource);
        services.TryAddSingleton(provider =>
        {
            var registry = new CompositionNodeRegistry();
            foreach (var contributor in provider.GetServices<ICompositionNodeRegistryContributor>())
            {
                contributor.Configure(registry);
            }

            return registry;
        });

        services.TryAddSingleton<CompositionRuntimeHost>();
        services.TryAddSingleton<ICompositionRuntimeHost>(
            provider => provider.GetRequiredService<CompositionRuntimeHost>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, CompositionRuntimeHostedService>());

        return new CompositionHostingBuilder(services);
    }
}
