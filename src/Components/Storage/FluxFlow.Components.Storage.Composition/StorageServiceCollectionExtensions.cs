using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Nodes;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Composition;
using FluxFlow.Data;

namespace FluxFlow.Components.Storage.Composition;

public static class StorageServiceCollectionExtensions
{
    public static FluxFlowRegistrationBuilder AddStorage(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddComponent(StorageComponents.StoragePut)
            .AddComponent(StorageComponents.StorageGet)
            .AddComponent(StorageComponents.StorageQuery)
            .AddComponent(StorageComponents.StorageDelete);
    }

    internal static void ConfigurePut(RuntimeComponentRegistrationBuilder component)
    {
        component.AddResource<IStorageStore>(StorageComponentDefinition.Resources.Store, isRequired: true, valueTypeHint: "IStorageStore or IStorageStoreFactory");
        component.AddResource<TimeProvider>(StorageComponentDefinition.Resources.Clock);
        component.AddOption<string>(StorageComponentDefinition.Options.Collection);
        component.AddOption<StorageWriteMode>(StorageComponentDefinition.Options.Mode);
        component.AddOption<bool>(StorageComponentDefinition.Options.EmitStoredRecord);
        component.AddOption<int>(StorageComponentDefinition.Options.BoundedCapacity);
        component
            .UseFactory(CreateStoragePutNode)
            .HasStandardPorts(new FluxFlow.Components.Storage.Materialization.StorageContentPutRequestMaterializer().InputShape);
    }

    internal static void ConfigureGet(RuntimeComponentRegistrationBuilder component)
    {
        component.AddResource<IStorageStore>(StorageComponentDefinition.Resources.Store, isRequired: true, valueTypeHint: "IStorageStore or IStorageStoreFactory");
        component.AddResource<TimeProvider>(StorageComponentDefinition.Resources.Clock);
        component.AddOption<string>(StorageComponentDefinition.Options.Collection);
        component.AddOption<bool>(StorageComponentDefinition.Options.IncludeExpired);
        component.AddOption<int>(StorageComponentDefinition.Options.BoundedCapacity);
        component
            .UseFactory(CreateStorageGetNode)
            .HasStandardPorts(new FluxFlow.Components.Storage.Materialization.StorageGetRequestMaterializer().InputShape);
    }

    internal static void ConfigureQuery(RuntimeComponentRegistrationBuilder component)
    {
        component.AddResource<IStorageStore>(StorageComponentDefinition.Resources.Store, isRequired: true, valueTypeHint: "IStorageStore or IStorageStoreFactory");
        component.AddResource<TimeProvider>(StorageComponentDefinition.Resources.Clock);
        component.AddOption<string>(StorageComponentDefinition.Options.Collection);
        component.AddOption<bool>(StorageComponentDefinition.Options.IncludeExpired);
        component.AddOption<int>(StorageComponentDefinition.Options.Offset);
        component.AddOption<int>(StorageComponentDefinition.Options.Limit);
        component.AddOption<bool>(StorageComponentDefinition.Options.EmitRecordsInResult);
        component.AddOption<int>(StorageComponentDefinition.Options.BoundedCapacity);
        component
            .UseFactory(CreateStorageQueryNode)
            .HasStandardPorts(new FluxFlow.Components.Storage.Materialization.StorageQueryRequestMaterializer().InputShape);
    }

    internal static void ConfigureDelete(RuntimeComponentRegistrationBuilder component)
    {
        component.AddResource<IStorageStore>(StorageComponentDefinition.Resources.Store, isRequired: true, valueTypeHint: "IStorageStore or IStorageStoreFactory");
        component.AddResource<TimeProvider>(StorageComponentDefinition.Resources.Clock);
        component.AddOption<string>(StorageComponentDefinition.Options.Collection);
        component.AddOption<int>(StorageComponentDefinition.Options.BoundedCapacity);
        component
            .UseFactory(CreateStorageDeleteNode)
            .HasStandardPorts(new FluxFlow.Components.Storage.Materialization.StorageDeleteRequestMaterializer().InputShape);
    }

    private static async ValueTask<ComponentNodeActivation<StoragePutNode>> CreateStoragePutNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<StoragePutOptions>();
        var clock = context.GetResource<TimeProvider>(StorageComponentDefinition.Resources.Clock);
        var store = await ResolveStoreAsync(context, options.Collection)
            .ConfigureAwait(false);
        return await store.CreateActivationAsync(storageStore =>
                new StoragePutNode(storageStore, options, clock))
            .ConfigureAwait(false);
    }

    private static async ValueTask<ComponentNodeActivation<StorageGetNode>> CreateStorageGetNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<StorageGetOptions>();
        var clock = context.GetResource<TimeProvider>(StorageComponentDefinition.Resources.Clock);
        var store = await ResolveStoreAsync(context, options.Collection)
            .ConfigureAwait(false);
        return await store.CreateActivationAsync(storageStore =>
                new StorageGetNode(storageStore, options, clock))
            .ConfigureAwait(false);
    }

    private static async ValueTask<ComponentNodeActivation<StorageQueryNode>> CreateStorageQueryNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<StorageQueryOptions>();
        var clock = context.GetResource<TimeProvider>(StorageComponentDefinition.Resources.Clock);
        var store = await ResolveStoreAsync(context, options.Collection)
            .ConfigureAwait(false);
        return await store.CreateActivationAsync(storageStore =>
                new StorageQueryNode(storageStore, options, clock))
            .ConfigureAwait(false);
    }

    private static async ValueTask<ComponentNodeActivation<StorageDeleteNode>> CreateStorageDeleteNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<StorageDeleteOptions>();
        var clock = context.GetResource<TimeProvider>(StorageComponentDefinition.Resources.Clock);
        var store = await ResolveStoreAsync(context, options.Collection)
            .ConfigureAwait(false);
        return await store.CreateActivationAsync(storageStore =>
                new StorageDeleteNode(storageStore, options, clock))
            .ConfigureAwait(false);
    }

    private static ValueTask<ResolvedStorageStore> ResolveStoreAsync(
        ComponentActivationContext context,
        string? collection)
    {
        var key = context.GetRequiredResourceKey(StorageComponentDefinition.Resources.Store);
        return StorageCompositionStoreResolver.ResolveAsync(context, key, collection);
    }
}
