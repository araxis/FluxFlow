using FluxFlow.Engine.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluxFlow.Engine.DurableOutput;

public static class DurableOutputServiceCollectionExtensions
{
    public static IServiceCollection AddFluxFlowDurableOutput(
        this IServiceCollection services,
        Action<DurableOutputRegistrationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new DurableOutputRegistrationBuilder();
        configure(builder);
        var configuration = builder.Build();

        var existingConfiguration = services.LastOrDefault(static descriptor =>
            descriptor.ServiceType == typeof(DurableOutputConfiguration));
        if (existingConfiguration is not null)
        {
            if (existingConfiguration.ImplementationInstance is DurableOutputConfiguration current &&
                current.IsEquivalentTo(configuration))
            {
                return services;
            }

            throw new InvalidOperationException(
                "FluxFlow durable output capture is already registered with different declarations.");
        }

        if (services.Any(static descriptor =>
                descriptor.ServiceType == typeof(IApplicationOutputCaptureResolver)))
        {
            throw new InvalidOperationException(
                "FluxFlow durable output capture requires exclusive IApplicationOutputCaptureResolver ownership.");
        }

        services.AddSingleton(configuration);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IApplicationOutputCaptureResolver>(static provider =>
            new DurableOutputCaptureResolver(
                provider.GetRequiredService<DurableOutputConfiguration>(),
                GetRequiredStore(provider),
                provider.GetRequiredService<TimeProvider>()));
        return services;
    }

    private static IDurableOutputStore GetRequiredStore(IServiceProvider provider)
    {
        var stores = provider.GetServices<IDurableOutputStore>().Take(2).ToArray();
        return stores.Length switch
        {
            1 => stores[0],
            0 => throw new InvalidOperationException(
                "AddFluxFlowDurableOutput requires one IDurableOutputStore registration."),
            _ => throw new InvalidOperationException(
                "AddFluxFlowDurableOutput supports exactly one IDurableOutputStore registration.")
        };
    }
}
