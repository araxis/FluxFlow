using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Nodes;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;

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

    private static ValueTask<ComposedNode> CreateStoragePutNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<StoragePutOptions>();
        ValidateBoundedCapacity(options.BoundedCapacity);
        var store = context.GetRequiredResource<IStorageStore>(
            StorageCompositionResourceNames.Store);
        var clock = context.GetResource<TimeProvider>(
            StorageCompositionResourceNames.Clock);
        var node = new StoragePutNode(store, options, clock);

        return ValueTask.FromResult(ComposedNode.Create(
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
            errors: node.Errors));
    }

    private static ValueTask<ComposedNode> CreateStorageGetNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<StorageGetOptions>();
        ValidateBoundedCapacity(options.BoundedCapacity);
        var store = context.GetRequiredResource<IStorageStore>(
            StorageCompositionResourceNames.Store);
        var clock = context.GetResource<TimeProvider>(
            StorageCompositionResourceNames.Clock);
        var node = new StorageGetNode(store, options, clock);

        return ValueTask.FromResult(ComposedNode.Create(
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
            errors: node.Errors));
    }

    private static ValueTask<ComposedNode> CreateStorageQueryNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<StorageQueryOptions>();
        ValidateQueryOptions(options);
        var store = context.GetRequiredResource<IStorageStore>(
            StorageCompositionResourceNames.Store);
        var clock = context.GetResource<TimeProvider>(
            StorageCompositionResourceNames.Clock);
        var node = new StorageQueryNode(store, options, clock);

        return ValueTask.FromResult(ComposedNode.Create(
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
            errors: node.Errors));
    }

    private static ValueTask<ComposedNode> CreateStorageDeleteNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<StorageDeleteOptions>();
        ValidateBoundedCapacity(options.BoundedCapacity);
        var store = context.GetRequiredResource<IStorageStore>(
            StorageCompositionResourceNames.Store);
        var clock = context.GetResource<TimeProvider>(
            StorageCompositionResourceNames.Clock);
        var node = new StorageDeleteNode(store, options, clock);

        return ValueTask.FromResult(ComposedNode.Create(
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
            errors: node.Errors));
    }

    private static void ValidateQueryOptions(StorageQueryOptions options)
    {
        ValidateBoundedCapacity(options.BoundedCapacity);

        if (options.Offset < 0)
        {
            throw new InvalidOperationException(
                "storage.query configuration offset cannot be negative.");
        }

        if (options.Limit <= 0)
        {
            throw new InvalidOperationException(
                "storage.query configuration limit must be greater than zero.");
        }
    }

    private static void ValidateBoundedCapacity(int boundedCapacity)
    {
        if (boundedCapacity <= 0)
        {
            throw new InvalidOperationException(
                "boundedCapacity must be greater than zero.");
        }
    }
}
