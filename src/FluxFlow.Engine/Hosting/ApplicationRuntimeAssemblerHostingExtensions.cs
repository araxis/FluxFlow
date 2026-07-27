using FluxFlow.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluxFlow.Engine.Hosting;

internal static class ApplicationRuntimeServiceCollectionExtensions
{
    internal static IServiceCollection AddFluxFlowRuntime(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<ApplicationRuntimeAssemblerOptions>();
        services.TryAddSingleton<
            ICompositionProcessingProfileMapper,
            DefaultCompositionProcessingProfileMapper>();
        services.TryAddSingleton(static provider => new ComponentCatalog(
            provider.GetServices<ComponentDescriptor>()));
        services.TryAddSingleton<ApplicationRuntimeAssembler>();
        return services;
    }
}
