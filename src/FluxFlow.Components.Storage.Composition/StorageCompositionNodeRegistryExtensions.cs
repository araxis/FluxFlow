using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Nodes;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Storage.Composition;

public static class StorageCompositionNodeRegistryExtensions
{
    public static CompositionNodeRegistry RegisterStoragePut(
        this CompositionNodeRegistry registry,
        string nodeType = StorageCompositionNodeTypes.Put)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateStoragePutNode,
            inputs:
            [
                CompositionPorts.Metadata<StoragePutRequest>(
                    StorageCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<StorageResult>(
                    StorageCompositionPortNames.Output)
            ]);
    }

    public static CompositionNodeRegistry RegisterStorageGet(
        this CompositionNodeRegistry registry,
        string nodeType = StorageCompositionNodeTypes.Get)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateStorageGetNode,
            inputs:
            [
                CompositionPorts.Metadata<StorageGetRequest>(
                    StorageCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<StorageResult>(
                    StorageCompositionPortNames.Output),
                CompositionPorts.Metadata<StorageResult>(
                    StorageCompositionPortNames.Found),
                CompositionPorts.Metadata<StorageResult>(
                    StorageCompositionPortNames.NotFound)
            ]);
    }

    public static CompositionNodeRegistry RegisterStorageQuery(
        this CompositionNodeRegistry registry,
        string nodeType = StorageCompositionNodeTypes.Query)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateStorageQueryNode,
            inputs:
            [
                CompositionPorts.Metadata<StorageQueryRequest>(
                    StorageCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<StorageQueryResult>(
                    StorageCompositionPortNames.Output),
                CompositionPorts.Metadata<StorageRecord>(
                    StorageCompositionPortNames.Records)
            ]);
    }

    public static CompositionNodeRegistry RegisterStorageDelete(
        this CompositionNodeRegistry registry,
        string nodeType = StorageCompositionNodeTypes.Delete)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateStorageDeleteNode,
            inputs:
            [
                CompositionPorts.Metadata<StorageDeleteRequest>(
                    StorageCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<StorageResult>(
                    StorageCompositionPortNames.Output)
            ]);
    }

    private static async ValueTask<ComposedNode> CreateStoragePutNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<StoragePutOptions>();
        var clock = context.GetResource<TimeProvider>(
            StorageCompositionResourceNames.Clock);
        var store = await ResolveStoreAsync(context, options.Collection).ConfigureAwait(false);
        var node = new StoragePutNode(store.Store, options, clock);

        return ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<StoragePutRequest>(
                    StorageCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<StorageResult>(
                    StorageCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            errors: node.Errors,
            disposeAsync: store.DisposeAsync);
    }

    private static async ValueTask<ComposedNode> CreateStorageGetNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<StorageGetOptions>();
        var clock = context.GetResource<TimeProvider>(
            StorageCompositionResourceNames.Clock);
        var store = await ResolveStoreAsync(context, options.Collection).ConfigureAwait(false);
        var node = new StorageGetNode(store.Store, options, clock);

        return ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<StorageGetRequest>(
                    StorageCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<StorageResult>(
                    StorageCompositionPortNames.Output,
                    node.Output),
                CompositionPorts.Output<StorageResult>(
                    StorageCompositionPortNames.Found,
                    node.Found),
                CompositionPorts.Output<StorageResult>(
                    StorageCompositionPortNames.NotFound,
                    node.NotFound)
            ],
            events: node.Events,
            errors: node.Errors,
            disposeAsync: store.DisposeAsync);
    }

    private static async ValueTask<ComposedNode> CreateStorageQueryNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<StorageQueryOptions>();
        var clock = context.GetResource<TimeProvider>(
            StorageCompositionResourceNames.Clock);
        var store = await ResolveStoreAsync(context, options.Collection).ConfigureAwait(false);
        var node = new StorageQueryNode(store.Store, options, clock);

        return ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<StorageQueryRequest>(
                    StorageCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<StorageQueryResult>(
                    StorageCompositionPortNames.Output,
                    node.Output),
                CompositionPorts.Output<StorageRecord>(
                    StorageCompositionPortNames.Records,
                    node.Records)
            ],
            events: node.Events,
            errors: node.Errors,
            disposeAsync: store.DisposeAsync);
    }

    private static async ValueTask<ComposedNode> CreateStorageDeleteNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<StorageDeleteOptions>();
        var clock = context.GetResource<TimeProvider>(
            StorageCompositionResourceNames.Clock);
        var store = await ResolveStoreAsync(context, options.Collection).ConfigureAwait(false);
        var node = new StorageDeleteNode(store.Store, options, clock);

        return ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<StorageDeleteRequest>(
                    StorageCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<StorageResult>(
                    StorageCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            errors: node.Errors,
            disposeAsync: store.DisposeAsync);
    }

    private static async ValueTask<ResolvedStorageStore> ResolveStoreAsync(
        CompositionNodeFactoryContext context,
        string? collection)
    {
        var key = context.GetRequiredResourceKey(StorageCompositionResourceNames.Store);
        var store = context.Services.GetKeyedService<IStorageStore>(key);
        if (store is not null)
            return ResolvedStorageStore.Shared(store);

        var factory = context.Services.GetKeyedService<IStorageStoreFactory>(key);
        if (factory is null)
        {
            throw new InvalidOperationException(
                $"Node '{context.WorkflowName}.{context.NodeName}' resource " +
                $"'{StorageCompositionResourceNames.Store}' references '{key}', but no keyed " +
                $"{nameof(IStorageStore)} or {nameof(IStorageStoreFactory)} service is registered.");
        }

        var clock = context.GetResource<TimeProvider>(StorageCompositionResourceNames.Clock);
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

    private sealed class ResolvedStorageStore
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
}
