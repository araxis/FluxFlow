using FluxFlow.Composition.Model;
using FluxFlow.Engine.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FluxFlow.Engine;

public static class FluxFlowApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddFluxFlow(
        this IServiceCollection services,
        ApplicationDefinition definition,
        Action<FluxFlowApplicationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return services.AddFluxFlow(
            new StaticApplicationDefinitionSource(definition),
            configure);
    }

    public static IServiceCollection AddFluxFlow(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<FluxFlowApplicationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return services.AddFluxFlow(configuration, sectionName: null, configure);
    }

    public static IServiceCollection AddFluxFlow(
        this IServiceCollection services,
        IConfiguration configuration,
        string? sectionName = null,
        Action<FluxFlowApplicationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return services.AddFluxFlow(
            new ConfigurationApplicationDefinitionSource(configuration, sectionName),
            configure);
    }

    public static IServiceCollection AddFluxFlow<TDefinitionSource>(
        this IServiceCollection services,
        Action<FluxFlowApplicationOptions>? configure = null)
        where TDefinitionSource : class, IApplicationDefinitionSource
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<TDefinitionSource>();
        services.TryAddSingleton<IApplicationDefinitionSource>(static provider =>
            provider.GetRequiredService<TDefinitionSource>());
        return AddFluxFlowCore(services, configure);
    }

    public static IServiceCollection AddFluxFlow(
        this IServiceCollection services,
        IApplicationDefinitionSource definitionSource,
        Action<FluxFlowApplicationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(definitionSource);
        services.TryAddSingleton(definitionSource);
        return AddFluxFlowCore(services, configure);
    }

    private static IServiceCollection AddFluxFlowCore(
        IServiceCollection services,
        Action<FluxFlowApplicationOptions>? configure)
    {
        services.AddOptions<FluxFlowApplicationOptions>();
        if (configure is not null)
            services.Configure(configure);

        services.AddFluxFlowEngine();
        services.AddOptions<ApplicationRuntimeAssemblerOptions>()
            .Configure<Microsoft.Extensions.Options.IOptions<FluxFlowApplicationOptions>>(
                static (runtime, current) =>
            {
                runtime.InputCapacity = current.Value.InputCapacity;
                runtime.OutputCapacity = current.Value.OutputCapacity;
            });
        services.TryAddSingleton(static provider => new FluxFlowApplication(
            provider.GetRequiredService<IApplicationDefinitionSource>(),
            provider.GetRequiredService<ApplicationRuntimeAssembler>(),
            provider.GetRequiredService<ApplicationDefinitionNormalizer>(),
            provider.GetRequiredService<
                Microsoft.Extensions.Options.IOptions<FluxFlowApplicationOptions>>()));
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, FluxFlowApplicationHostedService>());
        return services;
    }
}
