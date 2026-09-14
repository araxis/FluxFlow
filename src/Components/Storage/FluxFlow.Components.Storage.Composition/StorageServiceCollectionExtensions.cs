using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Nodes;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Composition;
using FluxFlow.Data;

namespace FluxFlow.Components.Storage.Composition;

public static class StorageServiceCollectionExtensions
{
    public static IServiceCollection AddStorage(this IServiceCollection builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddComponent(StorageComponentDefinition.Types.Put, StorageServiceCollectionExtensions.CreateStoragePutNode)
            .AddComponent(StorageComponentDefinition.Types.Get, StorageServiceCollectionExtensions.CreateStorageGetNode)
            .AddComponent(StorageComponentDefinition.Types.Query, StorageServiceCollectionExtensions.CreateStorageQueryNode)
            .AddComponent(StorageComponentDefinition.Types.Delete, StorageServiceCollectionExtensions.CreateStorageDeleteNode);
    }

    internal static async ValueTask<IFlowNode> CreateStoragePutNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<StoragePutOptions>();
        var clock = context.GetResource<TimeProvider>(StorageComponentDefinition.Resources.Clock);
        var store = await ResolveStoreAsync(context, options.Collection)
            .ConfigureAwait(false);
        var node = await store.CreateNodeAsync(storageStore =>
                new StoragePutNode(storageStore, options, clock))
            .ConfigureAwait(false);
        context.SetLifetime(disposeAsync: store.DisposeAsync);
        context.HasInput(node.Input);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return node;
    }

    internal static async ValueTask<IFlowNode> CreateStorageGetNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<StorageGetOptions>();
        var clock = context.GetResource<TimeProvider>(StorageComponentDefinition.Resources.Clock);
        var store = await ResolveStoreAsync(context, options.Collection)
            .ConfigureAwait(false);
        var node = await store.CreateNodeAsync(storageStore =>
                new StorageGetNode(storageStore, options, clock))
            .ConfigureAwait(false);
        context.SetLifetime(disposeAsync: store.DisposeAsync);
        context.HasInput(node.Input);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return node;
    }

    internal static async ValueTask<IFlowNode> CreateStorageQueryNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<StorageQueryOptions>();
        var clock = context.GetResource<TimeProvider>(StorageComponentDefinition.Resources.Clock);
        var store = await ResolveStoreAsync(context, options.Collection)
            .ConfigureAwait(false);
        var node = await store.CreateNodeAsync(storageStore =>
                new StorageQueryNode(storageStore, options, clock))
            .ConfigureAwait(false);
        context.SetLifetime(disposeAsync: store.DisposeAsync);
        context.HasInput(node.Input);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return node;
    }

    internal static async ValueTask<IFlowNode> CreateStorageDeleteNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<StorageDeleteOptions>();
        var clock = context.GetResource<TimeProvider>(StorageComponentDefinition.Resources.Clock);
        var store = await ResolveStoreAsync(context, options.Collection)
            .ConfigureAwait(false);
        var node = await store.CreateNodeAsync(storageStore =>
                new StorageDeleteNode(storageStore, options, clock))
            .ConfigureAwait(false);
        context.SetLifetime(disposeAsync: store.DisposeAsync);
        context.HasInput(node.Input);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return node;
    }

    private static ValueTask<ResolvedStorageStore> ResolveStoreAsync(
        ComponentActivationContext context,
        string? collection)
    {
        var key = context.GetRequiredResourceKey(StorageComponentDefinition.Resources.Store);
        return StorageCompositionStoreResolver.ResolveAsync(context, key, collection);
    }
}
