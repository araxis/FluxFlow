using FluxFlow.Composition.Hosting.Revisions;
using FluxFlow.Composition.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FluxFlow.Composition.Hosting;

public static class FluxFlowApplicationHostingServiceCollectionExtensions
{
    public static IServiceCollection AddFluxFlowApplication(
        this IServiceCollection services,
        ApplicationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(definition);
        return services.AddFluxFlowApplication(
            new StaticApplicationDefinitionSource(definition));
    }

    public static IServiceCollection AddFluxFlowApplication(
        this IServiceCollection services,
        IConfiguration configuration,
        string? sectionName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        return services.AddFluxFlowApplication(
            new ConfigurationApplicationDefinitionSource(configuration, sectionName));
    }

    public static IServiceCollection AddFluxFlowApplication(
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
            provider.GetService<IApplicationRevisionEventSink>(),
            provider.GetService<ApplicationDefinitionNormalizer>()));
        services.TryAddSingleton<IApplicationRevisionHost>(
            provider => provider.GetRequiredService<ApplicationRevisionHost>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, ApplicationRevisionHostedService>());

        return services;
    }

    public static IServiceCollection ConfigureFluxFlowApplication(
        this IServiceCollection services,
        Action<ApplicationRevisionHostingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        services.Configure(configure);
        return services;
    }

    public static IServiceCollection AddApplicationRevisionCandidateFactory<TFactory>(
        this IServiceCollection services)
        where TFactory : class, IApplicationRevisionCandidateFactory
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IApplicationRevisionCandidateFactory, TFactory>();
        return services;
    }

    public static IServiceCollection AddApplicationRevisionCandidateFactory(
        this IServiceCollection services,
        IApplicationRevisionCandidateFactory candidateFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(candidateFactory);
        services.TryAddSingleton(candidateFactory);
        return services;
    }

    public static IServiceCollection AddApplicationRevisionEventSink<TSink>(
        this IServiceCollection services)
        where TSink : class, IApplicationRevisionEventSink
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IApplicationRevisionEventSink, TSink>();
        return services;
    }

    public static IServiceCollection AddApplicationRevisionEventSink(
        this IServiceCollection services,
        IApplicationRevisionEventSink eventSink)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(eventSink);
        services.TryAddSingleton(eventSink);
        return services;
    }

    public static IServiceCollection AddApplicationResourceRegistrar<TRegistrar>(
        this IServiceCollection services)
        where TRegistrar : class, IApplicationResourceRegistrar
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IApplicationResourceRegistrar, TRegistrar>());
        return services;
    }

    public static IServiceCollection AddApplicationResourceRegistrar(
        this IServiceCollection services,
        IApplicationResourceRegistrar registrar)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(registrar);

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(IApplicationResourceRegistrar) &&
                ReferenceEquals(descriptor.ImplementationInstance, registrar)))
        {
            services.AddSingleton<IApplicationResourceRegistrar>(registrar);
        }

        return services;
    }

}
