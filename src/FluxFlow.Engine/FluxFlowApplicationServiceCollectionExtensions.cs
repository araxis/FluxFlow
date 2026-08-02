using FluxFlow.Composition.Model;
using FluxFlow.Composition;
using FluxFlow.Engine.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FluxFlow.Engine;

public static class FluxFlowApplicationServiceCollectionExtensions
{
    public static FluxFlowRegistrationBuilder AddFluxFlow(
        this IServiceCollection services,
        ApplicationDefinition definition,
        Action<FluxFlowApplicationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return services.AddFluxFlow(
            new StaticApplicationDefinitionSource(definition),
            configure);
    }

    public static FluxFlowRegistrationBuilder AddFluxFlow(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<FluxFlowApplicationOptions>? configure = null,
        string? sectionName = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return services.AddFluxFlow(
            new ConfigurationApplicationDefinitionSource(configuration, sectionName),
            configure);
    }

    public static FluxFlowRegistrationBuilder AddFluxFlow<TDefinitionSource>(
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

    public static FluxFlowRegistrationBuilder AddFluxFlow(
        this IServiceCollection services,
        IApplicationDefinitionSource definitionSource,
        Action<FluxFlowApplicationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(definitionSource);
        services.TryAddSingleton(definitionSource);
        return AddFluxFlowCore(services, configure);
    }

    private static FluxFlowRegistrationBuilder AddFluxFlowCore(
        IServiceCollection services,
        Action<FluxFlowApplicationOptions>? configure)
    {
        var applicationOptions = services
            .AddOptions<FluxFlowApplicationOptions>()
            .Validate(
                static options => !string.IsNullOrWhiteSpace(options.InitialRevisionId),
                $"{nameof(FluxFlowApplicationOptions.InitialRevisionId)} cannot be empty.")
            .Validate(
                static options => options.InputCapacity > 0,
                $"{nameof(FluxFlowApplicationOptions.InputCapacity)} must be greater than zero.")
            .Validate(
                static options => options.OutputCapacity > 0,
                $"{nameof(FluxFlowApplicationOptions.OutputCapacity)} must be greater than zero.");
        if (configure is not null)
            applicationOptions.Configure(configure);

        services.AddFluxFlowRuntime();
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
            provider.GetRequiredService<
                Microsoft.Extensions.Options.IOptions<FluxFlowApplicationOptions>>()));
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, FluxFlowApplicationHostedService>());
        return services.AddFluxFlowComponents();
    }
}
