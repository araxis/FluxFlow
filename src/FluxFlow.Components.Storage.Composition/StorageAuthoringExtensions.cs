using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Composition.Authoring;

namespace FluxFlow.Components.Storage.Composition;

public static class StorageAuthoringExtensions
{
    public static InputOutputComponentHandle<StorageContentPutRequest, StoragePutOutcome> AddStoragePut(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<StoragePutComponentBuilder> configure)
        => Add<StorageContentPutRequest, StoragePutOutcome, StoragePutComponentBuilder>(
            workflow, name, StorageComponentDefinition.Types.Put, configure, static (builder, definition) => builder.Apply(definition));

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
        => Add<StorageGetRequest, StorageGetOutcome, StorageGetComponentBuilder>(
            workflow, name, StorageComponentDefinition.Types.Get, configure, static (builder, definition) => builder.Apply(definition));

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
        => Add<StorageQueryRequest, StorageQueryOutcome, StorageQueryComponentBuilder>(
            workflow, name, StorageComponentDefinition.Types.Query, configure, static (builder, definition) => builder.Apply(definition));

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
        => Add<StorageDeleteRequest, StorageDeleteOutcome, StorageDeleteComponentBuilder>(
            workflow, name, StorageComponentDefinition.Types.Delete, configure, static (builder, definition) => builder.Apply(definition));

    public static WorkflowDefinitionBuilder AddStorageDelete(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<StorageDeleteComponentBuilder> configure,
        out InputOutputComponentHandle<StorageDeleteRequest, StorageDeleteOutcome> delete)
    {
        delete = workflow.AddStorageDelete(name, configure);
        return workflow;
    }

    private static InputOutputComponentHandle<TInput, TOutput> Add<TInput, TOutput, TBuilder>(
        WorkflowDefinitionBuilder workflow,
        string name,
        string type,
        Action<TBuilder> configure,
        Action<TBuilder, ComponentDefinitionBuilder> apply)
        where TBuilder : StorageComponentBuilder, new()
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(configure);
        var component = workflow.AddComponent(name, type, definition =>
        {
            var builder = new TBuilder();
            configure(builder);
            apply(builder, definition);
        });
        return new(component, StorageComponentDefinition.Ports.Input, StorageComponentDefinition.Ports.Output);
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
