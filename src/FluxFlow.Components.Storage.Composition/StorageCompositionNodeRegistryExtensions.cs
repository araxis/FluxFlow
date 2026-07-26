using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Nodes;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Composition;
using FluxFlow.Data;

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
                CompositionPorts.Metadata<StorageContentPutRequest>(
                    StorageCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<StoragePutOutcome>(
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
                CompositionPorts.Metadata<StorageGetOutcome>(
                    StorageCompositionPortNames.Output)
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
                CompositionPorts.Metadata<StorageQueryOutcome>(
                    StorageCompositionPortNames.Output)
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
                CompositionPorts.Metadata<StorageDeleteOutcome>(
                    StorageCompositionPortNames.Output)
            ]);
    }

    private static async ValueTask<ComposedNode> CreateStoragePutNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<StoragePutOptions>();
        var clock = context.GetResource<TimeProvider>(StorageCompositionResourceNames.Clock);
        var store = await ResolveStoreAsync(context, options.Collection)
            .ConfigureAwait(false);
        var node = new StoragePutNode(store.Store, options, clock);
        return ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<StorageContentPutRequest>(
                    StorageCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<StoragePutOutcome>(
                    StorageCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            disposeAsync: store.DisposeAsync);
    }

    private static async ValueTask<ComposedNode> CreateStorageGetNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<StorageGetOptions>();
        var clock = context.GetResource<TimeProvider>(StorageCompositionResourceNames.Clock);
        var store = await ResolveStoreAsync(context, options.Collection)
            .ConfigureAwait(false);
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
                CompositionPorts.Output<StorageGetOutcome>(
                    StorageCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            disposeAsync: store.DisposeAsync);
    }

    private static async ValueTask<ComposedNode> CreateStorageQueryNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<StorageQueryOptions>();
        var clock = context.GetResource<TimeProvider>(StorageCompositionResourceNames.Clock);
        var store = await ResolveStoreAsync(context, options.Collection)
            .ConfigureAwait(false);
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
                CompositionPorts.Output<StorageQueryOutcome>(
                    StorageCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            disposeAsync: store.DisposeAsync);
    }

    private static async ValueTask<ComposedNode> CreateStorageDeleteNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<StorageDeleteOptions>();
        var clock = context.GetResource<TimeProvider>(StorageCompositionResourceNames.Clock);
        var store = await ResolveStoreAsync(context, options.Collection)
            .ConfigureAwait(false);
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
                CompositionPorts.Output<StorageDeleteOutcome>(
                    StorageCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            disposeAsync: store.DisposeAsync);
    }

    private static ValueTask<ResolvedStorageStore> ResolveStoreAsync(
        CompositionNodeFactoryContext context,
        string? collection)
    {
        var key = context.GetRequiredResourceKey(StorageCompositionResourceNames.Store);
        return StorageCompositionStoreResolver.ResolveAsync(context, key, collection);
    }
}
