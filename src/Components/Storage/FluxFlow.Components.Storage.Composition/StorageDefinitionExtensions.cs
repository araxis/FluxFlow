using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Composition;
using FluxFlow.Composition.Definition;

using FluxFlow.Data;

namespace FluxFlow.Components.Storage.Composition;

public static class StorageDefinitionExtensions
{
    public static InputOutputComponentHandle<FlowValue, FlowValue> AddStoragePut(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<StoragePutComponentBuilder> configure)
        => workflow.AddComponent(name, StorageComponentDefinition.Types.Put,
            StorageServiceCollectionExtensions.CreateStoragePutNode,
            definition =>
            {
                var options = new StoragePutComponentBuilder();
                ArgumentNullException.ThrowIfNull(configure);
                configure(options);
                options.Apply(definition);
            },
            static component => new InputOutputComponentHandle<FlowValue, FlowValue>(component, StorageComponentDefinition.Ports.Input, StorageComponentDefinition.Ports.Output, StorageComponentDefinition.Ports.Events));

    public static WorkflowDefinitionBuilder AddStoragePut(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<StoragePutComponentBuilder> configure,
        out InputOutputComponentHandle<FlowValue, FlowValue> put)
    {
        put = workflow.AddStoragePut(name, configure);
        return workflow;
    }

    public static InputOutputComponentHandle<FlowValue, FlowValue> AddStorageGet(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<StorageGetComponentBuilder> configure)
        => workflow.AddComponent(name, StorageComponentDefinition.Types.Get,
            StorageServiceCollectionExtensions.CreateStorageGetNode,
            definition =>
            {
                var options = new StorageGetComponentBuilder();
                ArgumentNullException.ThrowIfNull(configure);
                configure(options);
                options.Apply(definition);
            },
            static component => new InputOutputComponentHandle<FlowValue, FlowValue>(component, StorageComponentDefinition.Ports.Input, StorageComponentDefinition.Ports.Output, StorageComponentDefinition.Ports.Events));

    public static WorkflowDefinitionBuilder AddStorageGet(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<StorageGetComponentBuilder> configure,
        out InputOutputComponentHandle<FlowValue, FlowValue> get)
    {
        get = workflow.AddStorageGet(name, configure);
        return workflow;
    }

    public static InputOutputComponentHandle<FlowValue, FlowValue> AddStorageQuery(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<StorageQueryComponentBuilder> configure)
        => workflow.AddComponent(name, StorageComponentDefinition.Types.Query,
            StorageServiceCollectionExtensions.CreateStorageQueryNode,
            definition =>
            {
                var options = new StorageQueryComponentBuilder();
                ArgumentNullException.ThrowIfNull(configure);
                configure(options);
                options.Apply(definition);
            },
            static component => new InputOutputComponentHandle<FlowValue, FlowValue>(component, StorageComponentDefinition.Ports.Input, StorageComponentDefinition.Ports.Output, StorageComponentDefinition.Ports.Events));

    public static WorkflowDefinitionBuilder AddStorageQuery(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<StorageQueryComponentBuilder> configure,
        out InputOutputComponentHandle<FlowValue, FlowValue> query)
    {
        query = workflow.AddStorageQuery(name, configure);
        return workflow;
    }

    public static InputOutputComponentHandle<FlowValue, FlowValue> AddStorageDelete(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<StorageDeleteComponentBuilder> configure)
        => workflow.AddComponent(name, StorageComponentDefinition.Types.Delete,
            StorageServiceCollectionExtensions.CreateStorageDeleteNode,
            definition =>
            {
                var options = new StorageDeleteComponentBuilder();
                ArgumentNullException.ThrowIfNull(configure);
                configure(options);
                options.Apply(definition);
            },
            static component => new InputOutputComponentHandle<FlowValue, FlowValue>(component, StorageComponentDefinition.Ports.Input, StorageComponentDefinition.Ports.Output, StorageComponentDefinition.Ports.Events));

    public static WorkflowDefinitionBuilder AddStorageDelete(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<StorageDeleteComponentBuilder> configure,
        out InputOutputComponentHandle<FlowValue, FlowValue> delete)
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
