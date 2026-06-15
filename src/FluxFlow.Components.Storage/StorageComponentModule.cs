using FluxFlow.Components.Storage.Nodes;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Storage;

public sealed class StorageComponentModule : IFlowNodeModule
{
    public StorageComponentModule(StorageComponentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Registrations =
        [
            new FlowNodeRegistration(
                StorageComponentTypes.Put,
                context => StorageNodeFactory.CreatePut(context, options)),
            new FlowNodeRegistration(
                StorageComponentTypes.Get,
                context => StorageNodeFactory.CreateGet(context, options)),
            new FlowNodeRegistration(
                StorageComponentTypes.Query,
                context => StorageNodeFactory.CreateQuery(context, options)),
            new FlowNodeRegistration(
                StorageComponentTypes.Delete,
                context => StorageNodeFactory.CreateDelete(context, options))
        ];
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}
