using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Engine.DurableOutput.TSql;

public static class TSqlDurableOutputServiceCollectionExtensions
{
    public static IServiceCollection AddFluxFlowTSqlDurableOutput(
        this IServiceCollection services,
        Action<TSqlDurableOutputStoreOptionsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new TSqlDurableOutputStoreOptionsBuilder();
        configure(builder);
        var options = builder.Build();

        var owned = services.Where(IsProviderService).ToArray();
        if (owned.Length != 0)
        {
            if (IsEquivalentRegistration(services, owned, options))
                return services;

            throw new InvalidOperationException(
                "T-SQL durable output is already registered with different options or service ownership.");
        }

        ThrowIfContractOwned(services, typeof(IDurableOutputStore));
        ThrowIfContractOwned(services, typeof(IDurableOutputDeliveryStore));
        ThrowIfContractOwned(services, typeof(IDurableOutputDeadLetterStore));
        ThrowIfContractOwned(services, typeof(IDurableOutputStatusStore));
        ThrowIfContractOwned(services, typeof(IDurableOutputRetentionStore));

        services.AddSingleton(options);
        services.AddSingleton<TSqlDurableOutputStore>();
        services.AddSingleton<IDurableOutputStore>(ResolveOutputStore);
        services.AddSingleton<IDurableOutputDeliveryStore>(ResolveDeliveryStore);
        services.AddSingleton<IDurableOutputDeadLetterStore>(ResolveDeadLetterStore);
        services.AddSingleton<IDurableOutputStatusStore>(ResolveStatusStore);
        services.AddSingleton<IDurableOutputRetentionStore>(ResolveRetentionStore);
        return services;
    }

    private static bool IsProviderService(ServiceDescriptor descriptor)
        => descriptor.ServiceType == typeof(TSqlDurableOutputStoreOptions) ||
           descriptor.ServiceType == typeof(TSqlDurableOutputStore);

    private static bool IsEquivalentRegistration(
        IServiceCollection services,
        IReadOnlyList<ServiceDescriptor> owned,
        TSqlDurableOutputStoreOptions options)
        => owned.Count == 2 &&
           owned.SingleOrDefault(static descriptor =>
               descriptor.ServiceType == typeof(TSqlDurableOutputStoreOptions)) is { } optionsDescriptor &&
           optionsDescriptor.Lifetime == ServiceLifetime.Singleton &&
           optionsDescriptor.ImplementationInstance is TSqlDurableOutputStoreOptions current &&
           current == options &&
           owned.SingleOrDefault(static descriptor =>
               descriptor.ServiceType == typeof(TSqlDurableOutputStore)) is { } storeDescriptor &&
           storeDescriptor.Lifetime == ServiceLifetime.Singleton &&
           storeDescriptor.ImplementationType == typeof(TSqlDurableOutputStore) &&
           HasExactAlias(services, typeof(IDurableOutputStore), nameof(ResolveOutputStore)) &&
           HasExactAlias(services, typeof(IDurableOutputDeliveryStore), nameof(ResolveDeliveryStore)) &&
           HasExactAlias(services, typeof(IDurableOutputDeadLetterStore), nameof(ResolveDeadLetterStore)) &&
           HasExactAlias(services, typeof(IDurableOutputStatusStore), nameof(ResolveStatusStore)) &&
           HasExactAlias(services, typeof(IDurableOutputRetentionStore), nameof(ResolveRetentionStore));

    private static bool HasExactAlias(
        IServiceCollection services,
        Type contract,
        string resolverName)
    {
        var descriptors = services.Where(descriptor => descriptor.ServiceType == contract).ToArray();
        if (descriptors.Length != 1)
            return false;

        var descriptor = descriptors[0];
        var factory = descriptor.ImplementationFactory;
        return descriptor.Lifetime == ServiceLifetime.Singleton &&
               factory is not null &&
               factory.Method.DeclaringType ==
                   typeof(TSqlDurableOutputServiceCollectionExtensions) &&
               factory.Method.Name == resolverName;
    }

    private static void ThrowIfContractOwned(IServiceCollection services, Type contract)
    {
        if (services.Any(descriptor => descriptor.ServiceType == contract))
        {
            throw new InvalidOperationException(
                $"T-SQL durable output cannot be registered because {contract.Name} is already registered.");
        }
    }

    private static IDurableOutputStore ResolveOutputStore(IServiceProvider provider)
        => provider.GetRequiredService<TSqlDurableOutputStore>();

    private static IDurableOutputDeliveryStore ResolveDeliveryStore(IServiceProvider provider)
        => provider.GetRequiredService<TSqlDurableOutputStore>();

    private static IDurableOutputDeadLetterStore ResolveDeadLetterStore(IServiceProvider provider)
        => provider.GetRequiredService<TSqlDurableOutputStore>();

    private static IDurableOutputStatusStore ResolveStatusStore(IServiceProvider provider)
        => provider.GetRequiredService<TSqlDurableOutputStore>();

    private static IDurableOutputRetentionStore ResolveRetentionStore(IServiceProvider provider)
        => provider.GetRequiredService<TSqlDurableOutputStore>();
}
