using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluxFlow.Composition;

public static class ApplicationResourceServiceCollectionExtensions
{
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
