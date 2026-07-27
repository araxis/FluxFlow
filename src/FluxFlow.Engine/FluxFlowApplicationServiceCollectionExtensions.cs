using FluxFlow.Composition.Hosting;
using FluxFlow.Composition.Hosting.Revisions;
using FluxFlow.Composition.Model;
using FluxFlow.Engine.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using LegacyDefinitionSource = FluxFlow.Composition.Hosting.IApplicationDefinitionSource;

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
        services.TryAddSingleton<LegacyDefinitionSource>(static provider =>
            new LegacyDefinitionSourceAdapter(
                provider.GetRequiredService<IApplicationDefinitionSource>()));
        services.AddOptions<ApplicationRevisionHostingOptions>()
            .Configure<IOptions<FluxFlowApplicationOptions>>(static (legacy, current) =>
            {
                legacy.InitialRevisionId = current.Value.InitialRevisionId;
                legacy.StartApplicationWithHost = false;
                legacy.StopApplicationWithHost = false;
            });
        services.AddOptions<ApplicationRuntimeAssemblerOptions>()
            .Configure<IOptions<FluxFlowApplicationOptions>>(static (runtime, current) =>
            {
                runtime.InputCapacity = current.Value.InputCapacity;
                runtime.OutputCapacity = current.Value.OutputCapacity;
            });
        services.TryAddSingleton(static provider => new ApplicationRevisionHost(
            provider.GetRequiredService<LegacyDefinitionSource>(),
            provider.GetRequiredService<IApplicationRevisionCandidateFactory>(),
            provider.GetRequiredService<IOptions<ApplicationRevisionHostingOptions>>(),
            provider.GetService<IApplicationRevisionEventSink>(),
            provider.GetService<ApplicationDefinitionNormalizer>()));
        services.TryAddSingleton(static provider => new FluxFlowApplication(
            provider.GetRequiredService<ApplicationRevisionHost>(),
            provider.GetRequiredService<IApplicationRuntimeAccess>(),
            provider.GetRequiredService<IOptions<FluxFlowApplicationOptions>>()));
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, FluxFlowApplicationHostedService>());
        return services;
    }

    private sealed class LegacyDefinitionSourceAdapter(
        IApplicationDefinitionSource source) : LegacyDefinitionSource
    {
        public ValueTask<ApplicationDefinition> LoadAsync(
            CancellationToken cancellationToken = default)
            => source.LoadAsync(cancellationToken);
    }
}
