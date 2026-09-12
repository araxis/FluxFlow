using FluxFlow.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FluxFlow.Engine;

/// <summary>Host integration for a directly constructed Core graph.</summary>
public static class FlowApplicationHostingExtensions
{
    /// <summary>
    /// Starts and drains the supplied graph with the host. The caller retains disposal
    /// ownership and must dispose the graph after host shutdown. Register one graph per host.
    /// </summary>
    public static IServiceCollection AddFluxFlow(this IServiceCollection services, FlowApplication application)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(application);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(FlowApplication)
            || descriptor.ServiceType == typeof(FluxFlowApplication)))
            throw new InvalidOperationException("A flow application is already registered for this host.");

        services.AddSingleton(application);
        services.AddSingleton<IHostedService, Hosting.FlowApplicationHostedService>();
        return services;
    }
}
