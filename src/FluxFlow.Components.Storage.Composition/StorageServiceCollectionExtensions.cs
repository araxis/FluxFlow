using FluxFlow.Components.Designer;
using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Nodes;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Composition;
using FluxFlow.Data;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Storage.Composition;

public static class StorageServiceCollectionExtensions
{
    internal static ComponentDescriptor PutDescriptor { get; } = CreateDescriptor<StorageContentPutRequest, StoragePutOutcome>(
        StorageComponentTypes.Put,
        CreateStoragePutNode);
    internal static ComponentDescriptor GetDescriptor { get; } = CreateDescriptor<StorageGetRequest, StorageGetOutcome>(
        StorageComponentTypes.Get,
        CreateStorageGetNode);
    internal static ComponentDescriptor QueryDescriptor { get; } = CreateDescriptor<StorageQueryRequest, StorageQueryOutcome>(
        StorageComponentTypes.Query,
        CreateStorageQueryNode);
    internal static ComponentDescriptor DeleteDescriptor { get; } = CreateDescriptor<StorageDeleteRequest, StorageDeleteOutcome>(
        StorageComponentTypes.Delete,
        CreateStorageDeleteNode);

    public static IServiceCollection AddStorageComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddFluxFlowComponent(PutDescriptor);
        services.AddFluxFlowComponent(GetDescriptor);
        services.AddFluxFlowComponent(QueryDescriptor);
        services.AddFluxFlowComponent(DeleteDescriptor);
        services.AddComponentDesignMetadataProvider<StorageComponentDesignMetadataProvider>();
        return services;
    }

    private static ComponentDescriptor CreateDescriptor<TInput, TOutput>(
        string type,
        ComponentFactory factory)
        => new(
            type,
            factory,
            inputs: [ComponentPorts.Metadata<TInput>(StorageComponentPortNames.Input)],
            outputs: [ComponentPorts.Metadata<TOutput>(StorageComponentPortNames.Output)]);

    private static async ValueTask<ComponentInstance> CreateStoragePutNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<StoragePutOptions>();
        var clock = context.GetResource<TimeProvider>(StorageComponentResourceNames.Clock);
        var store = await ResolveStoreAsync(context, options.Collection)
            .ConfigureAwait(false);
        var node = new StoragePutNode(store.Store, options, clock);
        return ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<StorageContentPutRequest>(
                    StorageComponentPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<StoragePutOutcome>(
                    StorageComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            disposeAsync: store.DisposeAsync);
    }

    private static async ValueTask<ComponentInstance> CreateStorageGetNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<StorageGetOptions>();
        var clock = context.GetResource<TimeProvider>(StorageComponentResourceNames.Clock);
        var store = await ResolveStoreAsync(context, options.Collection)
            .ConfigureAwait(false);
        var node = new StorageGetNode(store.Store, options, clock);
        return ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<StorageGetRequest>(
                    StorageComponentPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<StorageGetOutcome>(
                    StorageComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            disposeAsync: store.DisposeAsync);
    }

    private static async ValueTask<ComponentInstance> CreateStorageQueryNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<StorageQueryOptions>();
        var clock = context.GetResource<TimeProvider>(StorageComponentResourceNames.Clock);
        var store = await ResolveStoreAsync(context, options.Collection)
            .ConfigureAwait(false);
        var node = new StorageQueryNode(store.Store, options, clock);
        return ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<StorageQueryRequest>(
                    StorageComponentPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<StorageQueryOutcome>(
                    StorageComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            disposeAsync: store.DisposeAsync);
    }

    private static async ValueTask<ComponentInstance> CreateStorageDeleteNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<StorageDeleteOptions>();
        var clock = context.GetResource<TimeProvider>(StorageComponentResourceNames.Clock);
        var store = await ResolveStoreAsync(context, options.Collection)
            .ConfigureAwait(false);
        var node = new StorageDeleteNode(store.Store, options, clock);
        return ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<StorageDeleteRequest>(
                    StorageComponentPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<StorageDeleteOutcome>(
                    StorageComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            disposeAsync: store.DisposeAsync);
    }

    private static ValueTask<ResolvedStorageStore> ResolveStoreAsync(
        ComponentActivationContext context,
        string? collection)
    {
        var key = context.GetRequiredResourceKey(StorageComponentResourceNames.Store);
        return StorageCompositionStoreResolver.ResolveAsync(context, key, collection);
    }
}
