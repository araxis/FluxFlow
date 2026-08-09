using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Designer;
using FluxFlow.Composition.Authoring;

namespace FluxFlow.Components.Storage.Composition;

public static class StorageComponents
{
    public static ComponentContract<StoragePutComponentBuilder, InputOutputComponentHandle<StorageContentPutRequest, StoragePutOutcome>> StoragePut { get; } =
        Create<StoragePutComponentBuilder, StorageContentPutRequest, StoragePutOutcome>(StorageComponentDefinition.Types.Put, StorageServiceCollectionExtensions.ConfigurePut, static (options, definition) => options.Apply(definition));

    public static ComponentContract<StorageGetComponentBuilder, InputOutputComponentHandle<StorageGetRequest, StorageGetOutcome>> StorageGet { get; } =
        Create<StorageGetComponentBuilder, StorageGetRequest, StorageGetOutcome>(StorageComponentDefinition.Types.Get, StorageServiceCollectionExtensions.ConfigureGet, static (options, definition) => options.Apply(definition));

    public static ComponentContract<StorageQueryComponentBuilder, InputOutputComponentHandle<StorageQueryRequest, StorageQueryOutcome>> StorageQuery { get; } =
        Create<StorageQueryComponentBuilder, StorageQueryRequest, StorageQueryOutcome>(StorageComponentDefinition.Types.Query, StorageServiceCollectionExtensions.ConfigureQuery, static (options, definition) => options.Apply(definition));

    public static ComponentContract<StorageDeleteComponentBuilder, InputOutputComponentHandle<StorageDeleteRequest, StorageDeleteOutcome>> StorageDelete { get; } =
        Create<StorageDeleteComponentBuilder, StorageDeleteRequest, StorageDeleteOutcome>(StorageComponentDefinition.Types.Delete, StorageServiceCollectionExtensions.ConfigureDelete, static (options, definition) => options.Apply(definition));

    private static ComponentContract<TOptions, InputOutputComponentHandle<TInput, TOutput>> Create<TOptions, TInput, TOutput>(
        string type,
        Action<ComponentRegistrationBuilder> configure,
        Action<TOptions, ComponentDefinitionBuilder> apply)
        where TOptions : class, new()
        => DesignedComponentContract.Create(
            type,
            configure,
            static () => new TOptions(),
            apply,
            static component => new InputOutputComponentHandle<TInput, TOutput>(component, StorageComponentDefinition.Ports.Input, StorageComponentDefinition.Ports.Output, StorageComponentDefinition.Ports.Events));
}

public static class StorageAuthoringExtensions
{
    public static InputOutputComponentHandle<StorageContentPutRequest, StoragePutOutcome> AddStoragePut(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<StoragePutComponentBuilder> configure)
        => workflow.AddComponent(name, StorageComponents.StoragePut, configure);

    public static WorkflowDefinitionBuilder AddStoragePut(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<StoragePutComponentBuilder> configure,
        out InputOutputComponentHandle<StorageContentPutRequest, StoragePutOutcome> put)
    {
        put = workflow.AddStoragePut(name, configure);
        return workflow;
    }

    public static InputOutputComponentHandle<StorageGetRequest, StorageGetOutcome> AddStorageGet(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<StorageGetComponentBuilder> configure)
        => workflow.AddComponent(name, StorageComponents.StorageGet, configure);

    public static WorkflowDefinitionBuilder AddStorageGet(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<StorageGetComponentBuilder> configure,
        out InputOutputComponentHandle<StorageGetRequest, StorageGetOutcome> get)
    {
        get = workflow.AddStorageGet(name, configure);
        return workflow;
    }

    public static InputOutputComponentHandle<StorageQueryRequest, StorageQueryOutcome> AddStorageQuery(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<StorageQueryComponentBuilder> configure)
        => workflow.AddComponent(name, StorageComponents.StorageQuery, configure);

    public static WorkflowDefinitionBuilder AddStorageQuery(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<StorageQueryComponentBuilder> configure,
        out InputOutputComponentHandle<StorageQueryRequest, StorageQueryOutcome> query)
    {
        query = workflow.AddStorageQuery(name, configure);
        return workflow;
    }

    public static InputOutputComponentHandle<StorageDeleteRequest, StorageDeleteOutcome> AddStorageDelete(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<StorageDeleteComponentBuilder> configure)
        => workflow.AddComponent(name, StorageComponents.StorageDelete, configure);

    public static WorkflowDefinitionBuilder AddStorageDelete(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<StorageDeleteComponentBuilder> configure,
        out InputOutputComponentHandle<StorageDeleteRequest, StorageDeleteOutcome> delete)
    {
        delete = workflow.AddStorageDelete(name, configure);
        return workflow;
    }

}

public abstract class StorageComponentBuilder
{
    public string? Collection { get; set; }
    public int? BoundedCapacity { get; set; }
    public ResourceHandle<IStorageStore>? Store { get; set; }
    public ResourceHandle<TimeProvider>? Clock { get; set; }

    private protected void ApplyCommon(ComponentDefinitionBuilder definition)
    {
        if (Store is null)
            throw new InvalidOperationException("Storage components require Store.");
        Set(definition, StorageComponentDefinition.Options.Collection, Collection);
        Set(definition, StorageComponentDefinition.Options.BoundedCapacity, BoundedCapacity);
        definition.UseResource(StorageComponentDefinition.Resources.Store, Store);
        if (Clock is not null)
            definition.UseResource(StorageComponentDefinition.Resources.Clock, Clock);
    }

    private protected static void Set<T>(ComponentDefinitionBuilder definition, string name, T? value)
    {
        if (value is not null)
            definition.Set(name, value);
    }
}

public sealed class StoragePutComponentBuilder : StorageComponentBuilder
{
    public StorageWriteMode? Mode { get; set; }
    public bool? EmitStoredRecord { get; set; }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        ApplyCommon(definition);
        Set(definition, StorageComponentDefinition.Options.Mode, Mode);
        Set(definition, StorageComponentDefinition.Options.EmitStoredRecord, EmitStoredRecord);
    }
}

public sealed class StorageGetComponentBuilder : StorageComponentBuilder
{
    public bool? IncludeExpired { get; set; }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        ApplyCommon(definition);
        Set(definition, StorageComponentDefinition.Options.IncludeExpired, IncludeExpired);
    }
}

public sealed class StorageQueryComponentBuilder : StorageComponentBuilder
{
    public bool? IncludeExpired { get; set; }
    public int? Offset { get; set; }
    public int? Limit { get; set; }
    public bool? EmitRecordsInResult { get; set; }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        ApplyCommon(definition);
        Set(definition, StorageComponentDefinition.Options.IncludeExpired, IncludeExpired);
        Set(definition, StorageComponentDefinition.Options.Offset, Offset);
        Set(definition, StorageComponentDefinition.Options.Limit, Limit);
        Set(definition, StorageComponentDefinition.Options.EmitRecordsInResult, EmitRecordsInResult);
    }
}

public sealed class StorageDeleteComponentBuilder : StorageComponentBuilder
{
    internal void Apply(ComponentDefinitionBuilder definition) => ApplyCommon(definition);
}
