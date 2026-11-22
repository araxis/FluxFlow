using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluxFlow.Application;

/// <summary>Host integration for an application registered in native DI.</summary>
public static class FlowApplicationHostingExtensions
{
    /// <summary>
    /// Composes and starts the registered application when the host starts, then drains it
    /// when the host stops. The host provider owns component and resource disposal.
    /// </summary>
    /// <remarks>Repeated calls reuse the existing application and hosted-service registrations.</remarks>
    public static IServiceCollection AddFluxFlow(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<FlowApplication>();
        services.AddHostedService<FlowApplicationHostedService>();
        return services;
    }
}
