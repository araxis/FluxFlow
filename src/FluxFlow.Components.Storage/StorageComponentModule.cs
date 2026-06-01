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
                context => StoragePutNode.Create(context, options)),
            new FlowNodeRegistration(
                StorageComponentTypes.Get,
                context => StorageGetNode.Create(context, options)),
            new FlowNodeRegistration(
                StorageComponentTypes.Delete,
                context => StorageDeleteNode.Create(context, options))
        ];
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}
