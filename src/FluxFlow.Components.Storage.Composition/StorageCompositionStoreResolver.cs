using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Storage.Composition;

internal static class StorageCompositionStoreResolver
{
    public static async ValueTask<ResolvedStorageStore> ResolveAsync(
        ComponentActivationContext context,
        string key,
        string? collection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var store = context.Services.GetKeyedService<IStorageStore>(key);
        if (store is not null)
            return ResolvedStorageStore.Shared(store);

        var factory = context.Services.GetKeyedService<IStorageStoreFactory>(key);
        if (factory is null)
        {
            throw new InvalidOperationException(
                $"Component '{context.WorkflowName}.{context.ComponentName}' resource " +
                $"'{StorageComponentResourceNames.Store}' references '{key}', but no keyed " +
                $"{nameof(IStorageStore)} or {nameof(IStorageStoreFactory)} service is registered.");
        }

        var clock = context.GetResource<TimeProvider>(StorageComponentResourceNames.Clock);
        var lease = await factory
            .OpenAsync(new StorageStoreContext
            {
                StoreName = key,
                Collection = collection,
                Clock = clock ?? TimeProvider.System
            })
            .ConfigureAwait(false);
        return ResolvedStorageStore.Leased(lease);
    }
}

internal sealed class ResolvedStorageStore
{
    private readonly StorageStoreLease? _lease;

    private ResolvedStorageStore(IStorageStore store, StorageStoreLease? lease)
    {
        Store = store ?? throw new ArgumentNullException(nameof(store));
        _lease = lease;
    }

    public IStorageStore Store { get; }

    public static ResolvedStorageStore Shared(IStorageStore store)
        => new(store, lease: null);

    public static ResolvedStorageStore Leased(StorageStoreLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        return new ResolvedStorageStore(lease.Store, lease);
    }

    public ValueTask DisposeAsync()
        => _lease?.DisposeAsync() ?? ValueTask.CompletedTask;
}
