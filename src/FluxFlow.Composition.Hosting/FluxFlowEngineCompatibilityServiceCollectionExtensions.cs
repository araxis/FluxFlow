using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Engine.Hosting;

[Obsolete("AddFluxFlow registers the complete application runtime.")]
public static class FluxFlowEngineCompatibilityServiceCollectionExtensions
{
    public static IServiceCollection AddFluxFlowEngine(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
