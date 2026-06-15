using FluxFlow.Components.Storage.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Storage.Nodes;

internal static class StorageNodeFactory
{
    public static RuntimeNode CreatePut(
        RuntimeNodeFactoryContext context,
        StorageComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = StorageOptionsReader.ReadPutOptions(context.Definition);
        var node = new StoragePutNode(
            options,
            componentOptions,
            StorageNodeSupport.CreateStoreContext(
                context.Address,
                StorageComponentTypes.Put,
                options.Store,
                options.Collection,
                componentOptions.Clock));

        return context.CreateNode(node)
            .Input(StorageComponentPorts.Input, node.Input)
            .Output(StorageComponentPorts.Result, node.Result)
            .Output(StorageComponentPorts.Errors, node.Errors)
            .Build();
    }

    public static RuntimeNode CreateGet(
        RuntimeNodeFactoryContext context,
        StorageComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = StorageOptionsReader.ReadGetOptions(context.Definition);
        var node = new StorageGetNode(
            options,
            componentOptions,
            StorageNodeSupport.CreateStoreContext(
                context.Address,
                StorageComponentTypes.Get,
                options.Store,
                options.Collection,
                componentOptions.Clock));

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
        StorageComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = StorageOptionsReader.ReadDeleteOptions(context.Definition);
        var node = new StorageDeleteNode(
            options,
            componentOptions,
            StorageNodeSupport.CreateStoreContext(
                context.Address,
                StorageComponentTypes.Delete,
                options.Store,
                options.Collection,
                componentOptions.Clock));

        return context.CreateNode(node)
            .Input(StorageComponentPorts.Input, node.Input)
            .Output(StorageComponentPorts.Result, node.Result)
            .Output(StorageComponentPorts.Errors, node.Errors)
            .Build();
    }

    public static RuntimeNode CreateQuery(
        RuntimeNodeFactoryContext context,
        StorageComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = StorageOptionsReader.ReadQueryOptions(context.Definition);
        var node = new StorageQueryNode(
            options,
            componentOptions,
            StorageNodeSupport.CreateStoreContext(
                context.Address,
                StorageComponentTypes.Query,
                options.Store,
                options.Collection,
                componentOptions.Clock));

        return context.CreateNode(node)
            .Input(StorageComponentPorts.Input, node.Input)
            .Output(StorageComponentPorts.Result, node.Result)
            .Output(StorageComponentPorts.Records, node.Records)
            .Output(StorageComponentPorts.Errors, node.Errors)
            .Build();
    }
}
