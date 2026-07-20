using FluxFlow.Composition.Hosting.Revisions;
using FluxFlow.Composition.Model;
using FluxFlow.Composition.Revisions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FluxFlow.Composition.Hosting;

public static class FluxFlowApplicationHostingServiceCollectionExtensions
{
    public static ApplicationHostingBuilder AddFluxFlowApplication(
        this IServiceCollection services,
        ApplicationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(definition);
        return services.AddFluxFlowApplication(
            new StaticApplicationDefinitionSource(definition));
    }

    public static ApplicationHostingBuilder AddFluxFlowApplication(
        this IServiceCollection services,
        IConfiguration configuration,
        string? sectionName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        return services.AddFluxFlowApplication(
            new ConfigurationApplicationDefinitionSource(configuration, sectionName));
    }

    public static ApplicationHostingBuilder AddFluxFlowApplication(
        this IServiceCollection services,
        IApplicationDefinitionSource definitionSource)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(definitionSource);

        services.AddOptions<ApplicationRevisionHostingOptions>();
        services.TryAddSingleton(definitionSource);
        services.TryAddSingleton(provider => new ApplicationRevisionHost(
            provider.GetRequiredService<IApplicationDefinitionSource>(),
            provider.GetRequiredService<IApplicationRevisionCandidateFactory>(),
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ApplicationRevisionHostingOptions>>(),
            provider.GetService<IApplicationRevisionEventSink>()));
        services.TryAddSingleton<IApplicationRevisionHost>(
            provider => provider.GetRequiredService<ApplicationRevisionHost>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, ApplicationRevisionHostedService>());

        return new ApplicationHostingBuilder(services);
    }
}
