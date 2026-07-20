using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Composition;

namespace FluxFlow.Components.Storage.Composition;

public static class StorageTypedRegistrationExtensions
{
    public static CompositionNodeRegistry RegisterStoragePutResult(
        this CompositionNodeRegistry registry,
        string nodeType)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            StorageTypedNodeFactories.CreatePut,
            inputs: [CompositionPorts.Metadata<StoragePutRequest>(StorageCompositionPortNames.Input)],
            outputs: [CompositionPorts.Metadata<StorageResult>(StorageCompositionPortNames.Output)]);
    }

    public static CompositionNodeRegistry RegisterStorageGetResultBranches(
        this CompositionNodeRegistry registry,
        string nodeType)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            StorageTypedNodeFactories.CreateGet,
            inputs: [CompositionPorts.Metadata<StorageGetRequest>(StorageCompositionPortNames.Input)],
            outputs:
            [
                CompositionPorts.Metadata<StorageResult>(StorageCompositionPortNames.Output),
                CompositionPorts.Metadata<StorageResult>(StorageCompositionPortNames.Found),
                CompositionPorts.Metadata<StorageResult>(StorageCompositionPortNames.NotFound)
            ]);
    }

    public static CompositionNodeRegistry RegisterStorageQueryRecordOutputs(
        this CompositionNodeRegistry registry,
        string nodeType)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            StorageTypedNodeFactories.CreateQuery,
            inputs: [CompositionPorts.Metadata<StorageQueryRequest>(StorageCompositionPortNames.Input)],
            outputs:
            [
                CompositionPorts.Metadata<StorageQueryResult>(StorageCompositionPortNames.Output),
                CompositionPorts.Metadata<StorageRecord>(StorageCompositionPortNames.Records)
            ]);
    }

    public static CompositionNodeRegistry RegisterStorageDeleteResult(
        this CompositionNodeRegistry registry,
        string nodeType)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            StorageTypedNodeFactories.CreateDelete,
            inputs: [CompositionPorts.Metadata<StorageDeleteRequest>(StorageCompositionPortNames.Input)],
            outputs: [CompositionPorts.Metadata<StorageResult>(StorageCompositionPortNames.Output)]);
    }
}
