using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluxFlow.Components.Designer;

public static class ComponentDesignMetadataServiceCollectionExtensions
{
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
            ComponentDesignMetadataCatalog.FromProviders(
                provider.GetServices<IComponentDesignMetadataProvider>()));

        return services;
    }
}
