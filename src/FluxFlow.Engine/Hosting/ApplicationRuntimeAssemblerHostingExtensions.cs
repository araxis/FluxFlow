using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using FluxFlow.Composition.Hosting.Revisions;
using FluxFlow.Composition.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluxFlow.Engine.Hosting;

public static class FluxFlowEngineServiceCollectionExtensions
{
    public static IServiceCollection AddFluxFlowEngine(
        this IServiceCollection services,
        Action<ApplicationRuntimeAssemblerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<ApplicationRuntimeAssemblerOptions>();
        services.TryAddSingleton<
            ICompositionProcessingProfileMapper,
            DefaultCompositionProcessingProfileMapper>();
        services.TryAddSingleton(static provider => new ComponentCatalog(
            provider.GetServices<ComponentDescriptor>(),
            provider.GetServices<ResourceTypeAliasDescriptor>()));
        services.TryAddSingleton(static provider =>
            new ApplicationDefinitionNormalizer(
                provider.GetRequiredService<ComponentCatalog>()));
        services.TryAddSingleton<ApplicationRuntimeAssembler>();
        services.TryAddSingleton<IApplicationRuntimeAccess>(static provider =>
            provider.GetRequiredService<ApplicationRuntimeAssembler>());
        services.TryAddSingleton<IApplicationRevisionCandidateFactory>(static provider =>
            provider.GetRequiredService<ApplicationRuntimeAssembler>());
        services.TryAddSingleton<IApplicationRevisionEventSink>(static provider =>
            provider.GetRequiredService<ApplicationRuntimeAssembler>());

        if (configure is not null)
            services.Configure(configure);
        return services;
    }
}
