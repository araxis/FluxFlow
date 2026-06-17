using FluxFlow.Components.Storage.Nodes;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Storage;

public sealed class StorageComponentModule : IFlowNodeModule
{
    public StorageComponentModule(StorageComponentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var clock = options.Clock;
        var storeFactory = options.StoreFactory;
        Registrations =
        [
            new FlowNodeRegistration(
                StorageComponentTypes.Store,
                context => StorageStoreNodeFactory.Create(context, storeFactory, clock)),
            new FlowNodeRegistration(
                StorageComponentTypes.Put,
                context => StorageNodeFactory.CreatePut(context, clock)),
            new FlowNodeRegistration(
                StorageComponentTypes.Get,
                context => StorageNodeFactory.CreateGet(context, clock)),
            new FlowNodeRegistration(
                StorageComponentTypes.Query,
                context => StorageNodeFactory.CreateQuery(context, clock)),
            new FlowNodeRegistration(
                StorageComponentTypes.Delete,
                context => StorageNodeFactory.CreateDelete(context, clock))
        ];
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}
