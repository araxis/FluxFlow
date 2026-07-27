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

        _ = new ComponentCatalog(descriptors.Append(descriptor));
        services.AddSingleton(descriptor);
        AddCatalog(services);
        return services;
    }

    private static void AddCatalog(IServiceCollection services)
        => services.TryAddSingleton(static provider => new ComponentCatalog(
            provider.GetServices<ComponentDescriptor>()));

    private static T[] GetInstances<T>(IServiceCollection services)
        where T : class
        => services
            .Where(static service => service.ServiceType == typeof(T))
            .Select(static service => service.ImplementationInstance)
            .OfType<T>()
            .ToArray();
}
