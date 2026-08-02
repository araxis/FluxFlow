using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Engine.DurableOutput.SqlFile;

public static class SqlFileDurableOutputServiceCollectionExtensions
{
    public static IServiceCollection AddFluxFlowSqlFileDurableOutput(
        this IServiceCollection services,
        Action<SqlFileDurableOutputStoreOptionsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new SqlFileDurableOutputStoreOptionsBuilder();
        configure(builder);
        var options = builder.Build();

        var owned = services.Where(IsProviderService).ToArray();
        if (owned.Length != 0)
        {
            if (IsEquivalentRegistration(services, owned, options))
                return services;

            throw new InvalidOperationException(
                "SQL-file durable output is already registered with different options or service ownership.");
        }

        ThrowIfContractOwned(services, typeof(IDurableOutputStore));
        ThrowIfContractOwned(services, typeof(IDurableOutputDeliveryStore));
        ThrowIfContractOwned(services, typeof(IDurableOutputDeadLetterStore));
        ThrowIfContractOwned(services, typeof(IDurableOutputStatusStore));
        ThrowIfContractOwned(services, typeof(IDurableOutputRetentionStore));

        services.AddSingleton(options);
        services.AddSingleton<SqlFileDurableOutputStore>();
        services.AddSingleton<IDurableOutputStore>(ResolveOutputStore);
        services.AddSingleton<IDurableOutputDeliveryStore>(ResolveDeliveryStore);
        services.AddSingleton<IDurableOutputDeadLetterStore>(ResolveDeadLetterStore);
        services.AddSingleton<IDurableOutputStatusStore>(ResolveStatusStore);
        services.AddSingleton<IDurableOutputRetentionStore>(ResolveRetentionStore);
        return services;
    }

    private static bool IsProviderService(ServiceDescriptor descriptor)
        => descriptor.ServiceType == typeof(SqlFileDurableOutputStoreOptions) ||
           descriptor.ServiceType == typeof(SqlFileDurableOutputStore);

    private static bool IsEquivalentRegistration(
        IServiceCollection services,
        IReadOnlyList<ServiceDescriptor> owned,
        SqlFileDurableOutputStoreOptions options)
        => owned.Count == 2 &&
           owned.SingleOrDefault(static descriptor =>
               descriptor.ServiceType == typeof(SqlFileDurableOutputStoreOptions)) is { } optionsDescriptor &&
           optionsDescriptor.Lifetime == ServiceLifetime.Singleton &&
           optionsDescriptor.ImplementationInstance is SqlFileDurableOutputStoreOptions current &&
           current == options &&
           owned.SingleOrDefault(static descriptor =>
               descriptor.ServiceType == typeof(SqlFileDurableOutputStore)) is { } storeDescriptor &&
           storeDescriptor.Lifetime == ServiceLifetime.Singleton &&
           storeDescriptor.ImplementationType == typeof(SqlFileDurableOutputStore) &&
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
               factory.Method.DeclaringType == typeof(SqlFileDurableOutputServiceCollectionExtensions) &&
               factory.Method.Name == resolverName;
    }

    private static void ThrowIfContractOwned(IServiceCollection services, Type contract)
    {
        if (services.Any(descriptor => descriptor.ServiceType == contract))
        {
            throw new InvalidOperationException(
                $"SQL-file durable output cannot be registered because {contract.Name} is already registered.");
        }
    }

    private static IDurableOutputStore ResolveOutputStore(IServiceProvider provider)
        => provider.GetRequiredService<SqlFileDurableOutputStore>();

    private static IDurableOutputDeliveryStore ResolveDeliveryStore(IServiceProvider provider)
        => provider.GetRequiredService<SqlFileDurableOutputStore>();

    private static IDurableOutputDeadLetterStore ResolveDeadLetterStore(IServiceProvider provider)
        => provider.GetRequiredService<SqlFileDurableOutputStore>();

    private static IDurableOutputStatusStore ResolveStatusStore(IServiceProvider provider)
        => provider.GetRequiredService<SqlFileDurableOutputStore>();

    private static IDurableOutputRetentionStore ResolveRetentionStore(IServiceProvider provider)
        => provider.GetRequiredService<SqlFileDurableOutputStore>();
}
