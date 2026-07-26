using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluxFlow.Composition;

public static class FluxFlowComponentServiceCollectionExtensions
{
    public static IServiceCollection AddFluxFlowComponent(
        this IServiceCollection services,
        ComponentDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(descriptor);

        var descriptors = GetInstances<ComponentDescriptor>(services);
        if (descriptors.Any(existing => ReferenceEquals(existing, descriptor)))
            return services;

        _ = new ComponentCatalog(
            descriptors.Append(descriptor),
            GetInstances<ResourceTypeAliasDescriptor>(services));
        services.AddSingleton(descriptor);
        AddCatalog(services);
        return services;
    }

    public static IServiceCollection AddFluxFlowResourceTypeAlias(
        this IServiceCollection services,
        ResourceTypeAliasDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(descriptor);

        var aliases = GetInstances<ResourceTypeAliasDescriptor>(services);
        if (aliases.Any(existing => ReferenceEquals(existing, descriptor)))
            return services;
        if (aliases.Any(existing =>
                string.Equals(existing.Alias, descriptor.Alias, StringComparison.Ordinal) &&
                string.Equals(
                    existing.CanonicalType,
                    descriptor.CanonicalType,
                    StringComparison.Ordinal)))
        {
            return services;
        }

        _ = new ComponentCatalog(
            GetInstances<ComponentDescriptor>(services),
            aliases.Append(descriptor));
        services.AddSingleton(descriptor);
        AddCatalog(services);
        return services;
    }

    public static IServiceCollection AddFluxFlowResourceTypeAlias(
        this IServiceCollection services,
        string alias,
        string canonicalType)
        => services.AddFluxFlowResourceTypeAlias(
            new ResourceTypeAliasDescriptor(alias, canonicalType));

    private static void AddCatalog(IServiceCollection services)
        => services.TryAddSingleton(static provider => new ComponentCatalog(
            provider.GetServices<ComponentDescriptor>(),
            provider.GetServices<ResourceTypeAliasDescriptor>()));

    private static T[] GetInstances<T>(IServiceCollection services)
        where T : class
        => services
            .Where(static service => service.ServiceType == typeof(T))
            .Select(static service => service.ImplementationInstance)
            .OfType<T>()
            .ToArray();
}
