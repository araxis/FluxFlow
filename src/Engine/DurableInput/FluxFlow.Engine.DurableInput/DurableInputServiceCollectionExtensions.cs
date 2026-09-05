using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FluxFlow.Engine.DurableInput;

public static class DurableInputServiceCollectionExtensions
{
    public static IServiceCollection AddFluxFlowDurableInput(
        this IServiceCollection services,
        Action<DurableInputOptionsBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var builder = new DurableInputOptionsBuilder();
        configure?.Invoke(builder);
        var options = builder.Build();

        var existingOptions = services.LastOrDefault(static descriptor =>
            descriptor.ServiceType == typeof(DurableInputOptions));
        if (existingOptions is not null)
        {
            if (existingOptions.ImplementationInstance is DurableInputOptions current &&
                current == options &&
                services.Count(IsDispatcherDescriptor) == 1)
            {
                return services;
            }

            throw new InvalidOperationException(
                "FluxFlow durable input is already registered with different options or service ownership.");
        }

        if (services.Any(IsDispatcherDescriptor))
        {
            throw new InvalidOperationException(
                "FluxFlow durable input hosted-service ownership is already registered.");
        }

        services.TryAddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<DurableInputContractRegistry>();
        services.TryAddSingleton(static provider => new DurableApplicationInputs(
            GetRequiredStore(provider),
            provider.GetRequiredService<DurableInputContractRegistry>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetService<Microsoft.Extensions.Logging.ILogger<DurableApplicationInputs>>()));
        services.AddSingleton<IHostedService>(CreateDispatcher);
        return services;
    }

    public static IServiceCollection AddFluxFlowDurableInputContract<T>(
        this IServiceCollection services,
        string contractName)
        => AddContract(services, new DurableInputContract<T>(contractName, jsonTypeInfo: null));

    public static IServiceCollection AddFluxFlowDurableInputContract<T>(
        this IServiceCollection services,
        string contractName,
        JsonTypeInfo<T> jsonTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        return AddContract(services, new DurableInputContract<T>(contractName, jsonTypeInfo));
    }

    private static IServiceCollection AddContract(
        IServiceCollection services,
        IDurableInputContract contract)
    {
        ArgumentNullException.ThrowIfNull(services);

        foreach (var descriptor in services.Where(static item =>
                     item.ServiceType == typeof(IDurableInputContract)))
        {
            if (descriptor.ImplementationInstance is not IDurableInputContract existing)
            {
                throw new InvalidOperationException(
                    "A durable input contract was registered through an unsupported service descriptor.");
            }

            if (!string.Equals(existing.Name, contract.Name, StringComparison.Ordinal) &&
                existing.PayloadType != contract.PayloadType)
            {
                continue;
            }

            if (existing.IsEquivalentTo(contract))
                return services;

            throw new InvalidOperationException(
                $"Durable input contract '{contract.Name}' conflicts with an existing name or payload registration.");
        }

        services.AddSingleton(contract);
        return services;
    }

    private static IDurableInputStore GetRequiredStore(IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return GetRequiredStore(provider.GetServices<IDurableInputStore>());
    }

    private static IDurableInputStore GetRequiredStore(
        IEnumerable<IDurableInputStore> stores)
    {
        ArgumentNullException.ThrowIfNull(stores);
        var candidates = stores.Take(2).ToArray();
        return candidates.Length switch
        {
            1 => candidates[0],
            0 => throw new InvalidOperationException(
                "AddFluxFlowDurableInput requires one IDurableInputStore registration."),
            _ => throw new InvalidOperationException(
                "AddFluxFlowDurableInput supports exactly one IDurableInputStore registration.")
        };
    }

    private static IHostedService CreateDispatcher(IServiceProvider provider)
        => new DurableInputDispatcher(
            GetRequiredStore(provider),
            provider.GetServices<IDurableInputCompletionSource>(),
            provider.GetServices<IDurableInputLeaseRenewalStore>(),
            provider.GetRequiredService<DurableInputContractRegistry>(),
            provider.GetRequiredService<FluxFlowApplication>(),
            provider.GetRequiredService<DurableInputOptions>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DurableInputDispatcher>>());

    private static bool IsDispatcherDescriptor(ServiceDescriptor descriptor)
    {
        var factory = descriptor.ImplementationFactory;
        return descriptor.ServiceType == typeof(IHostedService) &&
               descriptor.Lifetime == ServiceLifetime.Singleton &&
               factory is not null &&
               factory.Method.DeclaringType == typeof(DurableInputServiceCollectionExtensions) &&
               factory.Method.Name == nameof(CreateDispatcher);
    }
}
