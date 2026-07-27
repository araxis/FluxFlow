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
    private static readonly Lazy<IReadOnlyCollection<ComponentDesignDeclaration>> DeclarationSet =
        new(CreateDeclarations);

    internal static IReadOnlyCollection<ComponentDesignDeclaration> Declarations =>
        DeclarationSet.Value;

    private static IReadOnlyCollection<ComponentDesignDeclaration> CreateDeclarations() =>
        ComponentDesignDeclaration.CreateRange(
            [
                PutDescriptor,
                GetDescriptor,
                QueryDescriptor,
                DeleteDescriptor
            ],
            StorageComponentDefinition.CreateMetadata());

    internal static ComponentDescriptor PutDescriptor { get; } = CreateDescriptor<StorageContentPutRequest, StoragePutOutcome>(
        StorageComponentDefinition.Types.Put,
        CreateStoragePutNode);
    internal static ComponentDescriptor GetDescriptor { get; } = CreateDescriptor<StorageGetRequest, StorageGetOutcome>(
        StorageComponentDefinition.Types.Get,
        CreateStorageGetNode);
    internal static ComponentDescriptor QueryDescriptor { get; } = CreateDescriptor<StorageQueryRequest, StorageQueryOutcome>(
        StorageComponentDefinition.Types.Query,
        CreateStorageQueryNode);
    internal static ComponentDescriptor DeleteDescriptor { get; } = CreateDescriptor<StorageDeleteRequest, StorageDeleteOutcome>(
        StorageComponentDefinition.Types.Delete,
        CreateStorageDeleteNode);

    public static IServiceCollection AddStorageComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddComponentDesignDeclarations(Declarations);
        return services;
    }

    private static ComponentDescriptor CreateDescriptor<TInput, TOutput>(
        string type,
        ComponentFactory factory)
        => new(
            type,
            factory,
            inputs: [ComponentPorts.Metadata<TInput>(StorageComponentDefinition.Ports.Input)],
            outputs: [ComponentPorts.Metadata<TOutput>(StorageComponentDefinition.Ports.Output)],
            options: StorageComponentDefinition.CreateOptions(type),
            resources: StorageComponentDefinition.CreateResources(type));

    private static async ValueTask<ComponentInstance> CreateStoragePutNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<StoragePutOptions>();
        var clock = context.GetResource<TimeProvider>(StorageComponentDefinition.Resources.Clock);
        var store = await ResolveStoreAsync(context, options.Collection)
            .ConfigureAwait(false);
        var node = new StoragePutNode(store.Store, options, clock);
        return ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<StorageContentPutRequest>(
                    StorageComponentDefinition.Ports.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<StoragePutOutcome>(
                    StorageComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events,
            disposeAsync: store.DisposeAsync);
    }

    private static async ValueTask<ComponentInstance> CreateStorageGetNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<StorageGetOptions>();
        var clock = context.GetResource<TimeProvider>(StorageComponentDefinition.Resources.Clock);
        var store = await ResolveStoreAsync(context, options.Collection)
            .ConfigureAwait(false);
        var node = new StorageGetNode(store.Store, options, clock);
        return ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<StorageGetRequest>(
                    StorageComponentDefinition.Ports.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<StorageGetOutcome>(
                    StorageComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events,
            disposeAsync: store.DisposeAsync);
    }

    private static async ValueTask<ComponentInstance> CreateStorageQueryNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<StorageQueryOptions>();
        var clock = context.GetResource<TimeProvider>(StorageComponentDefinition.Resources.Clock);
        var store = await ResolveStoreAsync(context, options.Collection)
            .ConfigureAwait(false);
        var node = new StorageQueryNode(store.Store, options, clock);
        return ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<StorageQueryRequest>(
                    StorageComponentDefinition.Ports.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<StorageQueryOutcome>(
                    StorageComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events,
            disposeAsync: store.DisposeAsync);
    }

    private static async ValueTask<ComponentInstance> CreateStorageDeleteNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<StorageDeleteOptions>();
        var clock = context.GetResource<TimeProvider>(StorageComponentDefinition.Resources.Clock);
        var store = await ResolveStoreAsync(context, options.Collection)
            .ConfigureAwait(false);
        var node = new StorageDeleteNode(store.Store, options, clock);
        return ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<StorageDeleteRequest>(
                    StorageComponentDefinition.Ports.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<StorageDeleteOutcome>(
                    StorageComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events,
            disposeAsync: store.DisposeAsync);
    }

    private static ValueTask<ResolvedStorageStore> ResolveStoreAsync(
        ComponentActivationContext context,
        string? collection)
    {
        var key = context.GetRequiredResourceKey(StorageComponentDefinition.Resources.Store);
        return StorageCompositionStoreResolver.ResolveAsync(context, key, collection);
    }
}
