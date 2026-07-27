using FluxFlow.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluxFlow.Components.Designer;

public static class ComponentDesignMetadataServiceCollectionExtensions
{
    public static IServiceCollection AddComponentDesignDeclaration(
        this IServiceCollection services,
        ComponentDesignDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(declaration);

        var declarations = GetInstances<ComponentDesignDeclaration>(services);
        if (declarations.Any(existing => ReferenceEquals(existing, declaration)))
            return services;

        if (declarations.Any(existing => string.Equals(
                existing.Descriptor.Type,
                declaration.Descriptor.Type,
                StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Component type '{declaration.Descriptor.Type}' has conflicting design declarations.");
        }

        services.AddFluxFlowComponent(declaration.Descriptor);
        services.AddSingleton(declaration);
        return services;
    }

    public static IServiceCollection AddComponentDesignMetadataProvider<TProvider>(
        this IServiceCollection services)
        where TProvider : class, IComponentDesignMetadataProvider
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IComponentDesignMetadataProvider, TProvider>());

        return services;
    }

    public static IServiceCollection AddComponentDesignMetadataProvider(
        this IServiceCollection services,
        IComponentDesignMetadataProvider provider)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(provider);

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IComponentDesignMetadataProvider>(provider));

        return services;
    }

    public static IServiceCollection AddComponentDesignMetadataCatalog(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(provider =>
            ComponentDesignMetadataCatalog.FromSources(
                provider.GetRequiredService<ComponentCatalog>(),
                provider.GetServices<IComponentDesignMetadataProvider>(),
                provider.GetServices<ComponentDesignDeclaration>()));

        return services;
    }

    private static T[] GetInstances<T>(IServiceCollection services)
        where T : class
        => services
            .Where(static service => service.ServiceType == typeof(T))
            .Select(static service => service.ImplementationInstance)
            .OfType<T>()
            .ToArray();
}
