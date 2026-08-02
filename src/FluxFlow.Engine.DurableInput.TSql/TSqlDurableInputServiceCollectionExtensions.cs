using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Engine.DurableInput.TSql;

public static class TSqlDurableInputServiceCollectionExtensions
{
    public static IServiceCollection AddFluxFlowTSqlDurableInput(
        this IServiceCollection services,
        Action<TSqlDurableInputStoreOptionsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new TSqlDurableInputStoreOptionsBuilder();
        configure(builder);
        var options = builder.Build();

        var owned = services.Where(IsProviderService).ToArray();
        if (owned.Length != 0)
        {
            if (IsEquivalentRegistration(services, owned, options))
                return services;

            throw new InvalidOperationException(
                "T-SQL durable input is already registered with different options or service ownership.");
        }

        ThrowIfContractOwned(services, typeof(IDurableInputStore));
        ThrowIfContractOwned(services, typeof(IDurableInputDeadLetterStore));
        ThrowIfContractOwned(services, typeof(IDurableInputLeaseRenewalStore));
        ThrowIfContractOwned(services, typeof(IDurableInputStatusStore));
        ThrowIfContractOwned(services, typeof(IDurableInputRetentionStore));

        services.AddSingleton(options);
        services.AddSingleton<TSqlDurableInputStore>();
        services.AddSingleton<IDurableInputStore>(ResolveInputStore);
        services.AddSingleton<IDurableInputDeadLetterStore>(ResolveDeadLetterStore);
        services.AddSingleton<IDurableInputLeaseRenewalStore>(ResolveLeaseRenewalStore);
        services.AddSingleton<IDurableInputStatusStore>(ResolveStatusStore);
        services.AddSingleton<IDurableInputRetentionStore>(ResolveRetentionStore);
        return services;
    }

    private static bool IsProviderService(ServiceDescriptor descriptor)
        => descriptor.ServiceType == typeof(TSqlDurableInputStoreOptions) ||
           descriptor.ServiceType == typeof(TSqlDurableInputStore);

    private static bool IsEquivalentRegistration(
        IServiceCollection services,
        IReadOnlyList<ServiceDescriptor> owned,
        TSqlDurableInputStoreOptions options)
        => owned.Count == 2 &&
           owned.SingleOrDefault(static descriptor =>
               descriptor.ServiceType == typeof(TSqlDurableInputStoreOptions)) is { } optionsDescriptor &&
           optionsDescriptor.Lifetime == ServiceLifetime.Singleton &&
           optionsDescriptor.ImplementationInstance is TSqlDurableInputStoreOptions current &&
           current == options &&
           owned.SingleOrDefault(static descriptor =>
               descriptor.ServiceType == typeof(TSqlDurableInputStore)) is { } storeDescriptor &&
           storeDescriptor.Lifetime == ServiceLifetime.Singleton &&
           storeDescriptor.ImplementationType == typeof(TSqlDurableInputStore) &&
           HasExactAlias(services, typeof(IDurableInputStore), nameof(ResolveInputStore)) &&
           HasExactAlias(services, typeof(IDurableInputDeadLetterStore), nameof(ResolveDeadLetterStore)) &&
           HasExactAlias(services, typeof(IDurableInputLeaseRenewalStore), nameof(ResolveLeaseRenewalStore)) &&
           HasExactAlias(services, typeof(IDurableInputStatusStore), nameof(ResolveStatusStore)) &&
           HasExactAlias(services, typeof(IDurableInputRetentionStore), nameof(ResolveRetentionStore));

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
               factory.Method.DeclaringType == typeof(TSqlDurableInputServiceCollectionExtensions) &&
               factory.Method.Name == resolverName;
    }

    private static void ThrowIfContractOwned(IServiceCollection services, Type contract)
    {
        if (services.Any(descriptor => descriptor.ServiceType == contract))
        {
            throw new InvalidOperationException(
                $"T-SQL durable input cannot be registered because {contract.Name} is already registered.");
        }
    }

    private static IDurableInputStore ResolveInputStore(IServiceProvider provider)
        => provider.GetRequiredService<TSqlDurableInputStore>();

    private static IDurableInputDeadLetterStore ResolveDeadLetterStore(IServiceProvider provider)
        => provider.GetRequiredService<TSqlDurableInputStore>();

    private static IDurableInputLeaseRenewalStore ResolveLeaseRenewalStore(IServiceProvider provider)
        => provider.GetRequiredService<TSqlDurableInputStore>();

    private static IDurableInputStatusStore ResolveStatusStore(IServiceProvider provider)
        => provider.GetRequiredService<TSqlDurableInputStore>();

    private static IDurableInputRetentionStore ResolveRetentionStore(IServiceProvider provider)
        => provider.GetRequiredService<TSqlDurableInputStore>();
}
