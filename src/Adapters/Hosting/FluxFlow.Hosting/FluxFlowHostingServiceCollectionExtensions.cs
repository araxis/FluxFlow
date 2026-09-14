using FluxFlow.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FluxFlow.Hosting;

/// <summary>
/// Registers <see cref="FlowApplication"/> applications with the .NET Generic Host.
/// </summary>
public static class FluxFlowHostingServiceCollectionExtensions
{
    /// <summary>
    /// Register a Core application so it runs as an <see cref="IHostedService"/>: the graph is built
    /// from <paramref name="build"/> and started when the host starts, drained on host stop, and
    /// disposed on shutdown. <paramref name="build"/> receives the application
    /// <see cref="IServiceProvider"/>, so its nodes can be resolved from DI. Call this more than
    /// once to host several flows in one application.
    /// </summary>
    public static IServiceCollection AddFlowApplication(
        this IServiceCollection services,
        Func<IServiceProvider, FlowApplication> build)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(build);

        // AddSingleton (not TryAdd): each call adds its own hosted service so multiple flows all run.
        services.AddSingleton<IHostedService>(provider => new FlowApplicationHostedService(provider, build));
        return services;
    }
}
