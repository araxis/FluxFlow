using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Nodes;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.Storage.Composition;

internal static class StorageTypedNodeFactories
{
    internal static async ValueTask<ComposedNode> CreatePut(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<StoragePutOptions>();
        var clock = context.GetResource<TimeProvider>(StorageCompositionResourceNames.Clock);
        var store = await ResolveAsync(context, options.Collection).ConfigureAwait(false);
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

    internal static async ValueTask<ComposedNode> CreateGet(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<StorageGetOptions>();
        var clock = context.GetResource<TimeProvider>(StorageCompositionResourceNames.Clock);
        var store = await ResolveAsync(context, options.Collection).ConfigureAwait(false);
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

    internal static async ValueTask<ComposedNode> CreateQuery(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<StorageQueryOptions>();
        var clock = context.GetResource<TimeProvider>(StorageCompositionResourceNames.Clock);
        var store = await ResolveAsync(context, options.Collection).ConfigureAwait(false);
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

    internal static async ValueTask<ComposedNode> CreateDelete(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<StorageDeleteOptions>();
        var clock = context.GetResource<TimeProvider>(StorageCompositionResourceNames.Clock);
        var store = await ResolveAsync(context, options.Collection).ConfigureAwait(false);
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

    private static ValueTask<ResolvedStorageStore> ResolveAsync(
        CompositionNodeFactoryContext context,
        string? collection)
    {
        var key = context.GetRequiredResourceKey(StorageCompositionResourceNames.Store);
        return StorageCompositionStoreResolver.ResolveAsync(context, key, collection);
    }
}
