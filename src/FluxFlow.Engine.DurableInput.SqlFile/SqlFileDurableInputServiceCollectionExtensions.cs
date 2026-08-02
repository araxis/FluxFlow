using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Engine.DurableInput.SqlFile;

public static class SqlFileDurableInputServiceCollectionExtensions
{
    public static IServiceCollection AddFluxFlowSqlFileDurableInput(
        this IServiceCollection services,
        Action<SqlFileDurableInputStoreOptionsBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var builder = new SqlFileDurableInputStoreOptionsBuilder();
        configure?.Invoke(builder);
        var options = builder.Build();

        var owned = services.Where(IsProviderService).ToArray();
        if (owned.Length != 0)
        {
            if (IsEquivalentRegistration(services, owned, options))
                return services;

            throw new InvalidOperationException(
                "SQL-file durable input is already registered with different options or service ownership.");
        }

        ThrowIfContractOwned(services, typeof(IDurableInputStore));
        ThrowIfContractOwned(services, typeof(IDurableInputDeadLetterStore));
        ThrowIfContractOwned(services, typeof(IDurableInputLeaseRenewalStore));
        ThrowIfContractOwned(services, typeof(IDurableInputStatusStore));
        ThrowIfContractOwned(services, typeof(IDurableInputRetentionStore));

        services.AddSingleton(options);
        services.AddSingleton<SqlFileDurableInputStore>();
        services.AddSingleton<IDurableInputStore>(ResolveInputStore);
        services.AddSingleton<IDurableInputDeadLetterStore>(ResolveDeadLetterStore);
        services.AddSingleton<IDurableInputLeaseRenewalStore>(ResolveLeaseRenewalStore);
        services.AddSingleton<IDurableInputStatusStore>(ResolveStatusStore);
        services.AddSingleton<IDurableInputRetentionStore>(ResolveRetentionStore);
        return services;
    }

    private static bool IsProviderService(ServiceDescriptor descriptor)
        => descriptor.ServiceType == typeof(SqlFileDurableInputStoreOptions) ||
           descriptor.ServiceType == typeof(SqlFileDurableInputStore);

    private static bool IsEquivalentRegistration(
        IServiceCollection services,
        IReadOnlyList<ServiceDescriptor> owned,
        SqlFileDurableInputStoreOptions options)
        => owned.Count == 2 &&
           owned.SingleOrDefault(static descriptor =>
               descriptor.ServiceType == typeof(SqlFileDurableInputStoreOptions)) is { } optionsDescriptor &&
           optionsDescriptor.Lifetime == ServiceLifetime.Singleton &&
           optionsDescriptor.ImplementationInstance is SqlFileDurableInputStoreOptions current &&
           current == options &&
           owned.SingleOrDefault(static descriptor =>
               descriptor.ServiceType == typeof(SqlFileDurableInputStore)) is { } storeDescriptor &&
           storeDescriptor.Lifetime == ServiceLifetime.Singleton &&
           storeDescriptor.ImplementationType == typeof(SqlFileDurableInputStore) &&
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
               factory.Method.DeclaringType == typeof(SqlFileDurableInputServiceCollectionExtensions) &&
               factory.Method.Name == resolverName;
    }

    private static void ThrowIfContractOwned(IServiceCollection services, Type contract)
    {
        if (services.Any(descriptor => descriptor.ServiceType == contract))
        {
            throw new InvalidOperationException(
                $"SQL-file durable input cannot be registered because {contract.Name} is already registered.");
        }
    }

    private static IDurableInputStore ResolveInputStore(IServiceProvider provider)
        => provider.GetRequiredService<SqlFileDurableInputStore>();

    private static IDurableInputDeadLetterStore ResolveDeadLetterStore(IServiceProvider provider)
        => provider.GetRequiredService<SqlFileDurableInputStore>();

    private static IDurableInputLeaseRenewalStore ResolveLeaseRenewalStore(IServiceProvider provider)
        => provider.GetRequiredService<SqlFileDurableInputStore>();

    private static IDurableInputStatusStore ResolveStatusStore(IServiceProvider provider)
        => provider.GetRequiredService<SqlFileDurableInputStore>();

    private static IDurableInputRetentionStore ResolveRetentionStore(IServiceProvider provider)
        => provider.GetRequiredService<SqlFileDurableInputStore>();
}
