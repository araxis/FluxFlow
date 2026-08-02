using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
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
            .AddComponent(StorageComponentDefinition.Types.Put, ConfigurePut)
            .AddComponent(StorageComponentDefinition.Types.Get, ConfigureGet)
            .AddComponent(StorageComponentDefinition.Types.Query, ConfigureQuery)
            .AddComponent(StorageComponentDefinition.Types.Delete, ConfigureDelete);
    }

    private static void ConfigurePut(ComponentRegistrationBuilder component)
    {
        var defaults = new StoragePutOptions();
        ConfigureCommon(component, CreateStoragePutNode, "Storage Put", "Stores exact content through a host-owned storage store.", "database-plus", "putRecord");
        AddCollection(component);
        component.AddOption<StorageWriteMode>(StorageComponentDefinition.Options.Mode, OptionValueKind.Enum, "Mode", "Write behavior when a record already exists or is missing.", defaultValue: defaults.Mode.ToString(), section: "Write", importance: OptionDesignMetadataAttributeValues.Primary);
        component.AddOptionChoice(StorageComponentDefinition.Options.Mode, StorageWriteMode.Upsert.ToString(), "Upsert", "Create or replace the record.");
        component.AddOptionChoice(StorageComponentDefinition.Options.Mode, StorageWriteMode.Create.ToString(), "Create", "Fail when the record already exists.");
        component.AddOptionChoice(StorageComponentDefinition.Options.Mode, StorageWriteMode.Replace.ToString(), "Replace", "Fail when the record does not exist.");
        component.AddOption<bool>(StorageComponentDefinition.Options.EmitStoredRecord, OptionValueKind.Boolean, "Emit Stored Record", "Include the stored record in the output result.", defaultValue: defaults.EmitStoredRecord, section: "Results", importance: OptionDesignMetadataAttributeValues.Advanced);
        AddCapacity(component, defaults.BoundedCapacity);
        AddPorts<StorageContentPutRequest, StoragePutOutcome>(component, "Exact-content storage put request.", "Stored or failed operation result.");
    }

    private static void ConfigureGet(ComponentRegistrationBuilder component)
    {
        var defaults = new StorageGetOptions();
        ConfigureCommon(component, CreateStorageGetNode, "Storage Get", "Reads exact content and returns found, missing, or failed results.", "database-search", "getRecord");
        AddCollection(component);
        AddIncludeExpired(component, defaults.IncludeExpired);
        AddCapacity(component, defaults.BoundedCapacity);
        AddPorts<StorageGetRequest, StorageGetOutcome>(component, "Storage get request.", "Found, missing, or failed operation result.");
    }

    private static void ConfigureQuery(ComponentRegistrationBuilder component)
    {
        var defaults = new StorageQueryOptions();
        ConfigureCommon(component, CreateStorageQueryNode, "Storage Query", "Queries exact-content records and returns one result.", "database", "queryRecords");
        AddCollection(component);
        AddIncludeExpired(component, defaults.IncludeExpired);
        component.AddOption<int>(StorageComponentDefinition.Options.Offset, OptionValueKind.Number, "Offset", "Number of matched records to skip.", defaultValue: defaults.Offset, min: 0, section: "Query", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        component.AddOption<int>(StorageComponentDefinition.Options.Limit, OptionValueKind.Number, "Limit", "Maximum number of records to return.", defaultValue: defaults.Limit, min: 1, section: "Query", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        component.AddOption<bool>(StorageComponentDefinition.Options.EmitRecordsInResult, OptionValueKind.Boolean, "Emit Records In Result", "Include matched records in the query result payload.", defaultValue: defaults.EmitRecordsInResult, section: "Results", importance: OptionDesignMetadataAttributeValues.Advanced);
        AddCapacity(component, defaults.BoundedCapacity);
        AddPorts<StorageQueryRequest, StorageQueryOutcome>(component, "Storage query request.", "Completed or failed storage query result.");
    }

    private static void ConfigureDelete(ComponentRegistrationBuilder component)
    {
        var defaults = new StorageDeleteOptions();
        ConfigureCommon(component, CreateStorageDeleteNode, "Storage Delete", "Deletes a record through a host-owned storage store.", "database-x", "deleteRecord");
        AddCollection(component);
        AddCapacity(component, defaults.BoundedCapacity);
        AddPorts<StorageDeleteRequest, StorageDeleteOutcome>(component, "Storage delete request.", "Deleted, missing, or failed operation result.");
    }

    private static void ConfigureCommon(ComponentRegistrationBuilder component, ComponentFactory factory, string displayName, string summary, string iconKey, string preferredNodeName)
    {
        component.UseFactory(factory);
        component.WithDisplay(displayName, "Storage", summary, iconKey, preferredNodeName, 460);
        component.AddResource<IStorageStore>(StorageComponentDefinition.Resources.Store, "Store", 0, "Required keyed storage store or store factory used for put, get, query, and delete operations.", true, "IStorageStore or IStorageStoreFactory", "IStorageStore or IStorageStoreFactory", ResourceDesignMetadataAttributeValues.HostOwned, ResourceDesignMetadataAttributeValues.Store, "storage-store:{name}");
        component.AddResource<TimeProvider>(StorageComponentDefinition.Resources.Clock, "Clock", 1, "Optional keyed clock for deterministic storage diagnostics and timestamps.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "clock:{name}");
    }

    private static void AddCollection(ComponentRegistrationBuilder component)
        => component.AddOption<string>(StorageComponentDefinition.Options.Collection, OptionValueKind.Text, "Collection", "Default collection used when the input request does not specify one.", section: "Collection", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Text);

    private static void AddIncludeExpired(ComponentRegistrationBuilder component, bool defaultValue)
        => component.AddOption<bool>(StorageComponentDefinition.Options.IncludeExpired, OptionValueKind.Boolean, "Include Expired", "Include records that the store considers expired.", defaultValue: defaultValue, section: "Expiration", importance: OptionDesignMetadataAttributeValues.Advanced);

    private static void AddCapacity(ComponentRegistrationBuilder component, int defaultValue)
        => component.AddOption<int>(StorageComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded processing and reliable normal-data output.", defaultValue: defaultValue, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);

    private static void AddPorts<TInput, TOutput>(ComponentRegistrationBuilder component, string inputSummary, string outputSummary)
    {
        component.AddInput<TInput>(StorageComponentDefinition.Ports.Input, "Input", "Messages", 0, inputSummary, true);
        component.AddOutput<TOutput>(StorageComponentDefinition.Ports.Output, "Output", "Results", 1, outputSummary, true);
    }

    private static async ValueTask<ComponentInstance> CreateStoragePutNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<StoragePutOptions>();
        var clock = context.GetResource<TimeProvider>(StorageComponentDefinition.Resources.Clock);
        var store = await ResolveStoreAsync(context, options.Collection)
            .ConfigureAwait(false);
        return await store.CreateInstanceAsync((storageStore, disposeAsync) =>
        {
            var node = new StoragePutNode(storageStore, options, clock);
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
                disposeAsync: disposeAsync);
        }).ConfigureAwait(false);
    }

    private static async ValueTask<ComponentInstance> CreateStorageGetNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<StorageGetOptions>();
        var clock = context.GetResource<TimeProvider>(StorageComponentDefinition.Resources.Clock);
        var store = await ResolveStoreAsync(context, options.Collection)
            .ConfigureAwait(false);
        return await store.CreateInstanceAsync((storageStore, disposeAsync) =>
        {
            var node = new StorageGetNode(storageStore, options, clock);
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
                disposeAsync: disposeAsync);
        }).ConfigureAwait(false);
    }

    private static async ValueTask<ComponentInstance> CreateStorageQueryNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<StorageQueryOptions>();
        var clock = context.GetResource<TimeProvider>(StorageComponentDefinition.Resources.Clock);
        var store = await ResolveStoreAsync(context, options.Collection)
            .ConfigureAwait(false);
        return await store.CreateInstanceAsync((storageStore, disposeAsync) =>
        {
            var node = new StorageQueryNode(storageStore, options, clock);
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
                disposeAsync: disposeAsync);
        }).ConfigureAwait(false);
    }

    private static async ValueTask<ComponentInstance> CreateStorageDeleteNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<StorageDeleteOptions>();
        var clock = context.GetResource<TimeProvider>(StorageComponentDefinition.Resources.Clock);
        var store = await ResolveStoreAsync(context, options.Collection)
            .ConfigureAwait(false);
        return await store.CreateInstanceAsync((storageStore, disposeAsync) =>
        {
            var node = new StorageDeleteNode(storageStore, options, clock);
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
                disposeAsync: disposeAsync);
        }).ConfigureAwait(false);
    }

    private static ValueTask<ResolvedStorageStore> ResolveStoreAsync(
        ComponentActivationContext context,
        string? collection)
    {
        var key = context.GetRequiredResourceKey(StorageComponentDefinition.Resources.Store);
        return StorageCompositionStoreResolver.ResolveAsync(context, key, collection);
    }
}
