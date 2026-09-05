using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.Designer;
using FluxFlow.Composition.Authoring;

using FluxFlow.Data;

namespace FluxFlow.Components.FileSystem.Composition;

public static class FileSystemComponents
{
    public static ComponentContract<FileReadComponentBuilder, InputOutputComponentHandle<FlowValue, FlowValue>> FileRead { get; } =
        DesignedComponentContract.Create(
            FileSystemComponentDefinition.Types.Read,
            FileSystemServiceCollectionExtensions.ConfigureRead,
            static () => new FileReadComponentBuilder(),
            static (options, definition) => options.Apply(definition),
            static component => new InputOutputComponentHandle<FlowValue, FlowValue>(component, FileSystemComponentDefinition.Ports.Input, FileSystemComponentDefinition.Ports.Output, FileSystemComponentDefinition.Ports.Events));

    public static ComponentContract<FileWriteComponentBuilder, InputOutputComponentHandle<FlowValue, FlowValue>> FileWrite { get; } =
        DesignedComponentContract.Create(
            FileSystemComponentDefinition.Types.Write,
            FileSystemServiceCollectionExtensions.ConfigureWrite,
            static () => new FileWriteComponentBuilder(),
            static (options, definition) => options.Apply(definition),
            static component => new InputOutputComponentHandle<FlowValue, FlowValue>(component, FileSystemComponentDefinition.Ports.Input, FileSystemComponentDefinition.Ports.Output, FileSystemComponentDefinition.Ports.Events));

    public static ComponentContract<DirectoryEnumerateComponentBuilder, OutputComponentHandle<FlowValue>> DirectoryEnumerate { get; } =
        DesignedComponentContract.Create(
            FileSystemComponentDefinition.Types.DirectoryEnumerate,
            FileSystemServiceCollectionExtensions.ConfigureDirectoryEnumerate,
            static () => new DirectoryEnumerateComponentBuilder(),
            static (options, definition) => options.Apply(definition),
            static component => new OutputComponentHandle<FlowValue>(component, FileSystemComponentDefinition.Ports.Output, FileSystemComponentDefinition.Ports.Events));

    public static ComponentContract<FileWatchComponentBuilder, OutputComponentHandle<FlowValue>> FileWatch { get; } =
        DesignedComponentContract.Create(
            FileSystemComponentDefinition.Types.Watch,
            FileSystemServiceCollectionExtensions.ConfigureWatch,
            static () => new FileWatchComponentBuilder(),
            static (options, definition) => options.Apply(definition),
            static component => new OutputComponentHandle<FlowValue>(component, FileSystemComponentDefinition.Ports.Output, FileSystemComponentDefinition.Ports.Events));
}

public static class FileSystemAuthoringExtensions
{
    public static InputOutputComponentHandle<FlowValue, FlowValue> AddFileRead(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<FileReadComponentBuilder> configure)
        => workflow.AddComponent(name, FileSystemComponents.FileRead, configure);

    public static WorkflowDefinitionBuilder AddFileRead(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<FileReadComponentBuilder> configure,
        out InputOutputComponentHandle<FlowValue, FlowValue> read)
    {
        read = workflow.AddFileRead(name, configure);
        return workflow;
    }

    public static InputOutputComponentHandle<FlowValue, FlowValue> AddFileWrite(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<FileWriteComponentBuilder> configure)
        => workflow.AddComponent(name, FileSystemComponents.FileWrite, configure);

    public static WorkflowDefinitionBuilder AddFileWrite(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<FileWriteComponentBuilder> configure,
        out InputOutputComponentHandle<FlowValue, FlowValue> write)
    {
        write = workflow.AddFileWrite(name, configure);
        return workflow;
    }

    public static OutputComponentHandle<FlowValue> AddDirectoryEnumerate(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<DirectoryEnumerateComponentBuilder> configure)
        => workflow.AddComponent(name, FileSystemComponents.DirectoryEnumerate, configure);

    public static WorkflowDefinitionBuilder AddDirectoryEnumerate(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<DirectoryEnumerateComponentBuilder> configure,
        out OutputComponentHandle<FlowValue> directory)
    {
        directory = workflow.AddDirectoryEnumerate(name, configure);
        return workflow;
    }

    public static OutputComponentHandle<FlowValue> AddFileWatch(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<FileWatchComponentBuilder> configure)
        => workflow.AddComponent(name, FileSystemComponents.FileWatch, configure);

    public static WorkflowDefinitionBuilder AddFileWatch(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<FileWatchComponentBuilder> configure,
        out OutputComponentHandle<FlowValue> watch)
    {
        watch = workflow.AddFileWatch(name, configure);
        return workflow;
    }

}

public abstract class FileSystemComponentBuilder
{
    public int? BoundedCapacity { get; set; }
    public string? BaseDirectory { get; set; }
    public bool? AllowAbsolutePaths { get; set; }
    public ResourceHandle<TimeProvider>? Clock { get; set; }

    private protected void ApplyCommon(ComponentDefinitionBuilder definition)
    {
        Set(definition, FileSystemComponentDefinition.Options.BoundedCapacity, BoundedCapacity);
        Set(definition, FileSystemComponentDefinition.Options.BaseDirectory, BaseDirectory);
        Set(definition, FileSystemComponentDefinition.Options.AllowAbsolutePaths, AllowAbsolutePaths);
        if (Clock is not null)
            definition.UseResource(FileSystemComponentDefinition.Resources.Clock, Clock);
    }

    private protected static void Set<T>(ComponentDefinitionBuilder definition, string name, T? value)
    {
        if (value is not null)
            definition.Set(name, value);
    }
}

public sealed class FileReadComponentBuilder : FileSystemComponentBuilder
{
    public string? DefaultEncoding { get; set; }
    public long? MaxBytes { get; set; }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        ApplyCommon(definition);
        Set(definition, FileSystemComponentDefinition.Options.DefaultEncoding, DefaultEncoding);
        Set(definition, FileSystemComponentDefinition.Options.MaxBytes, MaxBytes);
    }
}

public sealed class FileWriteComponentBuilder : FileSystemComponentBuilder
{
    internal void Apply(ComponentDefinitionBuilder definition) => ApplyCommon(definition);
}

public sealed class DirectoryEnumerateComponentBuilder : FileSystemComponentBuilder
{
    public string? Directory { get; set; }
    public string? Filter { get; set; }
    public bool? IncludeSubdirectories { get; set; }
    public bool? IncludeFiles { get; set; }
    public bool? IncludeDirectories { get; set; }
    public long? MaxEntries { get; set; }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        ApplyCommon(definition);
        Set(definition, FileSystemComponentDefinition.Options.Directory, Directory);
        Set(definition, FileSystemComponentDefinition.Options.Filter, Filter);
        Set(definition, FileSystemComponentDefinition.Options.IncludeSubdirectories, IncludeSubdirectories);
        Set(definition, FileSystemComponentDefinition.Options.IncludeFiles, IncludeFiles);
        Set(definition, FileSystemComponentDefinition.Options.IncludeDirectories, IncludeDirectories);
        Set(definition, FileSystemComponentDefinition.Options.MaxEntries, MaxEntries);
    }
}

public sealed class FileWatchComponentBuilder : FileSystemComponentBuilder
{
    public string? Directory { get; set; }
    public string? Filter { get; set; }
    public bool? IncludeSubdirectories { get; set; }
    public string[]? NotifyFilters { get; set; }
    public int? InternalBufferSize { get; set; }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        ApplyCommon(definition);
        Set(definition, FileSystemComponentDefinition.Options.Directory, Directory);
        Set(definition, FileSystemComponentDefinition.Options.Filter, Filter);
        Set(definition, FileSystemComponentDefinition.Options.IncludeSubdirectories, IncludeSubdirectories);
        Set(definition, FileSystemComponentDefinition.Options.NotifyFilters, NotifyFilters);
        Set(definition, FileSystemComponentDefinition.Options.InternalBufferSize, InternalBufferSize);
    }
}
