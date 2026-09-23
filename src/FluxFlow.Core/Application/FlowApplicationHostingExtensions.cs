using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FluxFlow.Core.Application;

/// <summary>Host integration for an application registered in native DI.</summary>
public static class FlowApplicationHostingExtensions
{
    /// <summary>
    /// Composes and starts the registered application when the host starts, then drains it
    /// when the host stops. The host provider owns component and resource disposal.
    /// </summary>
    public static IServiceCollection AddFluxFlow(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(descriptor => !descriptor.IsKeyedService && descriptor.ImplementationType == typeof(FlowApplicationHostedService)))
            throw new InvalidOperationException("A flow application is already registered for this host.");

        services.TryAddSingleton<FlowApplication>();
        services.AddSingleton<IHostedService, FlowApplicationHostedService>();
        return services;
    }
}
