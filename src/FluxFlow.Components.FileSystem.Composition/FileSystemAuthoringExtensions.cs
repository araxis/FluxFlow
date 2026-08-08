using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.Designer;
using FluxFlow.Composition.Authoring;

namespace FluxFlow.Components.FileSystem.Composition;

public static class FileSystemComponents
{
    public static ComponentContract<FileReadComponentBuilder, InputOutputComponentHandle<FileReadRequest, FileReadContent>> FileRead { get; } =
        DesignedComponentContract.Create(
            FileSystemComponentDefinition.Types.Read,
            FileSystemServiceCollectionExtensions.ConfigureRead,
            static () => new FileReadComponentBuilder(),
            static (options, definition) => options.Apply(definition),
            static component => new InputOutputComponentHandle<FileReadRequest, FileReadContent>(component, FileSystemComponentDefinition.Ports.Input, FileSystemComponentDefinition.Ports.Output, FileSystemComponentDefinition.Ports.Events));

    public static ComponentContract<FileWriteComponentBuilder, InputOutputComponentHandle<FileContentWriteRequest, FileWriteResult>> FileWrite { get; } =
        DesignedComponentContract.Create(
            FileSystemComponentDefinition.Types.Write,
            FileSystemServiceCollectionExtensions.ConfigureWrite,
            static () => new FileWriteComponentBuilder(),
            static (options, definition) => options.Apply(definition),
            static component => new InputOutputComponentHandle<FileContentWriteRequest, FileWriteResult>(component, FileSystemComponentDefinition.Ports.Input, FileSystemComponentDefinition.Ports.Output, FileSystemComponentDefinition.Ports.Events));

    public static ComponentContract<DirectoryEnumerateComponentBuilder, OutputComponentHandle<DirectoryEntry>> DirectoryEnumerate { get; } =
        DesignedComponentContract.Create(
            FileSystemComponentDefinition.Types.DirectoryEnumerate,
            FileSystemServiceCollectionExtensions.ConfigureDirectoryEnumerate,
            static () => new DirectoryEnumerateComponentBuilder(),
            static (options, definition) => options.Apply(definition),
            static component => new OutputComponentHandle<DirectoryEntry>(component, FileSystemComponentDefinition.Ports.Output, FileSystemComponentDefinition.Ports.Events));

    public static ComponentContract<FileWatchComponentBuilder, OutputComponentHandle<FileChange>> FileWatch { get; } =
        DesignedComponentContract.Create(
            FileSystemComponentDefinition.Types.Watch,
            FileSystemServiceCollectionExtensions.ConfigureWatch,
            static () => new FileWatchComponentBuilder(),
            static (options, definition) => options.Apply(definition),
            static component => new OutputComponentHandle<FileChange>(component, FileSystemComponentDefinition.Ports.Output, FileSystemComponentDefinition.Ports.Events));
}

public static class FileSystemAuthoringExtensions
{
    public static InputOutputComponentHandle<FileReadRequest, FileReadContent> AddFileRead(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<FileReadComponentBuilder> configure)
        => workflow.AddComponent(name, FileSystemComponents.FileRead, configure);

    public static WorkflowDefinitionBuilder AddFileRead(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<FileReadComponentBuilder> configure,
        out InputOutputComponentHandle<FileReadRequest, FileReadContent> read)
    {
        read = workflow.AddFileRead(name, configure);
        return workflow;
    }

    public static InputOutputComponentHandle<FileContentWriteRequest, FileWriteResult> AddFileWrite(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<FileWriteComponentBuilder> configure)
        => workflow.AddComponent(name, FileSystemComponents.FileWrite, configure);

    public static WorkflowDefinitionBuilder AddFileWrite(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<FileWriteComponentBuilder> configure,
        out InputOutputComponentHandle<FileContentWriteRequest, FileWriteResult> write)
    {
        write = workflow.AddFileWrite(name, configure);
        return workflow;
    }

    public static OutputComponentHandle<DirectoryEntry> AddDirectoryEnumerate(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<DirectoryEnumerateComponentBuilder> configure)
        => workflow.AddComponent(name, FileSystemComponents.DirectoryEnumerate, configure);

    public static WorkflowDefinitionBuilder AddDirectoryEnumerate(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<DirectoryEnumerateComponentBuilder> configure,
        out OutputComponentHandle<DirectoryEntry> directory)
    {
        directory = workflow.AddDirectoryEnumerate(name, configure);
        return workflow;
    }

    public static OutputComponentHandle<FileChange> AddFileWatch(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<FileWatchComponentBuilder> configure)
        => workflow.AddComponent(name, FileSystemComponents.FileWatch, configure);

    public static WorkflowDefinitionBuilder AddFileWatch(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<FileWatchComponentBuilder> configure,
        out OutputComponentHandle<FileChange> watch)
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
