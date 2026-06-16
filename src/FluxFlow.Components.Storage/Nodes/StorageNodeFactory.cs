using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Storage.Nodes;

internal static class StorageNodeFactory
{
    public static RuntimeNode CreatePut(
        RuntimeNodeFactoryContext context,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(clock);

        var options = StorageOptionsReader.ReadPutOptions(context.Definition);
        var store = ResolveStore(context, options.Store);
        var node = new StoragePutNode(options, store, clock);

        return context.CreateNode(node)
            .Input(StorageComponentPorts.Input, node.Input)
            .Output(StorageComponentPorts.Result, node.Result)
            .Output(StorageComponentPorts.Errors, node.Errors)
            .Build();
    }

    public static RuntimeNode CreateGet(
        RuntimeNodeFactoryContext context,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(clock);

        var options = StorageOptionsReader.ReadGetOptions(context.Definition);
        var store = ResolveStore(context, options.Store);
        var node = new StorageGetNode(options, store, clock);

        return context.CreateNode(node)
            .Input(StorageComponentPorts.Input, node.Input)
            .Output(StorageComponentPorts.Result, node.Result)
            .Output(StorageComponentPorts.Found, node.Found)
            .Output(StorageComponentPorts.NotFound, node.NotFound)
            .Output(StorageComponentPorts.Errors, node.Errors)
            .Build();
    }

    public static RuntimeNode CreateDelete(
        RuntimeNodeFactoryContext context,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(clock);

        var options = StorageOptionsReader.ReadDeleteOptions(context.Definition);
        var store = ResolveStore(context, options.Store);
        var node = new StorageDeleteNode(options, store, clock);

        return context.CreateNode(node)
            .Input(StorageComponentPorts.Input, node.Input)
            .Output(StorageComponentPorts.Result, node.Result)
            .Output(StorageComponentPorts.Errors, node.Errors)
            .Build();
    }

    public static RuntimeNode CreateQuery(
        RuntimeNodeFactoryContext context,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(clock);

        var options = StorageOptionsReader.ReadQueryOptions(context.Definition);
        var store = ResolveStore(context, options.Store);
        var node = new StorageQueryNode(options, store, clock);

        return context.CreateNode(node)
            .Input(StorageComponentPorts.Input, node.Input)
            .Output(StorageComponentPorts.Result, node.Result)
            .Output(StorageComponentPorts.Records, node.Records)
            .Output(StorageComponentPorts.Errors, node.Errors)
            .Build();
    }

    private static IStorageStoreHandle ResolveStore(
        RuntimeNodeFactoryContext context,
        string? store)
        => context.GetResource<IStorageStoreHandle>(new NodeName(store!));
}
