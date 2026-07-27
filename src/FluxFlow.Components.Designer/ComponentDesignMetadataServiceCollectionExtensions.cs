using FluxFlow.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluxFlow.Components.Designer;

public static class ComponentDesignMetadataServiceCollectionExtensions
{
    public static IServiceCollection AddComponentDesignDeclarations(
        this IServiceCollection services,
        IEnumerable<ComponentDesignDeclaration> declarations)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(declarations);

        foreach (var declaration in declarations)
            services.AddComponentDesignDeclaration(declaration);

        return services;
    }

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

    public static IServiceCollection AddComponentDesignMetadataCatalog(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(provider =>
            ComponentDesignMetadataCatalog.FromDeclarations(
                provider.GetRequiredService<ComponentCatalog>(),
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
