using FluxFlow.Core.Addressing;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Core.Application;

public static class ResourceRegistrationExtensions
{
    public static IServiceCollection AddFluxFlowResource<TService>(
        this IServiceCollection services,
        ApplicationAddress address,
        Func<IServiceProvider, TService> factory)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ValidateAddress(address);
        ArgumentNullException.ThrowIfNull(factory);
        services.AddKeyedSingleton<TService>(
            address.Value,
            (provider, _) => factory(provider));
        return services;
    }

    public static IServiceCollection AddExternalFluxFlowResource<TService>(
        this IServiceCollection services,
        ApplicationAddress address,
        TService service)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ValidateAddress(address);
        ArgumentNullException.ThrowIfNull(service);
        services.AddKeyedSingleton(address.Value, service);
        return services;
    }

    private static void ValidateAddress(ApplicationAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.Kind != ApplicationAddressKind.Resource)
        {
            throw new ArgumentException(
                $"Address '{address}' must be a {ApplicationAddressKind.Resource} address.",
                nameof(address));
        }
    }
}
