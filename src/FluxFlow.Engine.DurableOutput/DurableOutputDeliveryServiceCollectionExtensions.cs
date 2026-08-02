using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FluxFlow.Engine.DurableOutput;

public static class DurableOutputDeliveryServiceCollectionExtensions
{
    public static IServiceCollection AddFluxFlowDurableOutputDelivery(
        this IServiceCollection services,
        Action<DurableOutputDeliveryOptionsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new DurableOutputDeliveryOptionsBuilder();
        configure(builder);
        var options = builder.Build();

        var existingOptions = services.LastOrDefault(static descriptor =>
            descriptor.ServiceType == typeof(DurableOutputDeliveryOptions));
        if (existingOptions is not null)
        {
            if (existingOptions.ImplementationInstance is DurableOutputDeliveryOptions current &&
                current == options &&
                services.Count(IsDispatcherDescriptor) == 1)
            {
                return services;
            }

            throw new InvalidOperationException(
                "FluxFlow durable output delivery is already registered with different options or service ownership.");
        }

        if (services.Any(IsDispatcherDescriptor))
        {
            throw new InvalidOperationException(
                "FluxFlow durable output delivery hosted-service ownership is already registered.");
        }

        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, DurableOutputDeliveryDispatcher>());
        return services;
    }

    private static bool IsDispatcherDescriptor(ServiceDescriptor descriptor)
        => descriptor.ServiceType == typeof(IHostedService) &&
           descriptor.ImplementationType == typeof(DurableOutputDeliveryDispatcher);
}
