using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Nodes;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.FileSystem.Composition;

public static class FileSystemServiceCollectionExtensions
{
    public static FluxFlowRegistrationBuilder AddFileSystem(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddComponent(FileSystemComponents.FileRead)
            .AddComponent(FileSystemComponents.FileWrite)
            .AddComponent(FileSystemComponents.DirectoryEnumerate)
            .AddComponent(FileSystemComponents.FileWatch);
    }

    internal static void ConfigureRead(RuntimeComponentRegistrationBuilder component)
    {
        component.AddResource<TimeProvider>(FileSystemComponentDefinition.Resources.Clock);
        component.AddOption<int>(FileSystemComponentDefinition.Options.BoundedCapacity);
        component.AddOption<string>(FileSystemComponentDefinition.Options.BaseDirectory);
        component.AddOption<bool>(FileSystemComponentDefinition.Options.AllowAbsolutePaths);
        component.AddOption<string>(FileSystemComponentDefinition.Options.DefaultEncoding);
        component.AddOption<long?>(FileSystemComponentDefinition.Options.MaxBytes);
        component
            .UseFactory(CreateFileReadNode)
            .HasStandardPorts(new FluxFlow.Components.FileSystem.Materialization.FileReadRequestMaterializer().InputShape);
    }

    internal static void ConfigureWrite(RuntimeComponentRegistrationBuilder component)
    {
        component.AddResource<TimeProvider>(FileSystemComponentDefinition.Resources.Clock);
        component.AddOption<int>(FileSystemComponentDefinition.Options.BoundedCapacity);
        component.AddOption<string>(FileSystemComponentDefinition.Options.BaseDirectory);
        component.AddOption<bool>(FileSystemComponentDefinition.Options.AllowAbsolutePaths);
        component
            .UseFactory(CreateFileWriteNode)
            .HasStandardPorts(new FluxFlow.Components.FileSystem.Materialization.FileContentWriteRequestMaterializer().InputShape);
    }

    internal static void ConfigureDirectoryEnumerate(RuntimeComponentRegistrationBuilder component)
    {
        component.AddResource<TimeProvider>(FileSystemComponentDefinition.Resources.Clock);
        component.AddOption<int>(FileSystemComponentDefinition.Options.BoundedCapacity);
        component.AddOption<string>(FileSystemComponentDefinition.Options.Directory, isRequired: true);
        component.AddOption<string>(FileSystemComponentDefinition.Options.Filter, isRequired: true);
        component.AddOption<bool>(FileSystemComponentDefinition.Options.IncludeSubdirectories);
        component.AddOption<bool>(FileSystemComponentDefinition.Options.IncludeFiles);
        component.AddOption<bool>(FileSystemComponentDefinition.Options.IncludeDirectories);
        component.AddOption<string>(FileSystemComponentDefinition.Options.BaseDirectory);
        component.AddOption<bool>(FileSystemComponentDefinition.Options.AllowAbsolutePaths);
        component.AddOption<long?>(FileSystemComponentDefinition.Options.MaxEntries);
        component
            .UseFactory(CreateDirectoryEnumerateNode)
            .HasOutput(FileSystemComponentDefinition.Ports.Output, static node => node.Output)
            .HasEvents(FileSystemComponentDefinition.Ports.Events, static node => node.Events);
    }

    internal static void ConfigureWatch(RuntimeComponentRegistrationBuilder component)
    {
        component.AddResource<TimeProvider>(FileSystemComponentDefinition.Resources.Clock);
        component.AddOption<int>(FileSystemComponentDefinition.Options.BoundedCapacity);
        component.AddOption<string>(FileSystemComponentDefinition.Options.Directory, isRequired: true);
        component.AddOption<string>(FileSystemComponentDefinition.Options.BaseDirectory);
        component.AddOption<bool>(FileSystemComponentDefinition.Options.AllowAbsolutePaths);
        component.AddOption<string>(FileSystemComponentDefinition.Options.Filter, isRequired: true);
        component.AddOption<bool>(FileSystemComponentDefinition.Options.IncludeSubdirectories);
        component.AddOption<string[]>(FileSystemComponentDefinition.Options.NotifyFilters);
        component.AddOption<int?>(FileSystemComponentDefinition.Options.InternalBufferSize);
        component
            .UseFactory(CreateFileWatchNode)
            .HasOutput(FileSystemComponentDefinition.Ports.Output, static node => node.Output)
            .HasEvents(FileSystemComponentDefinition.Ports.Events, static node => node.Events);
    }

    private static FileReadNode CreateFileReadNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<FileReadOptions>();
        var clock = context.GetResource<TimeProvider>(
            FileSystemComponentDefinition.Resources.Clock);
        return new FileReadNode(options, clock);
    }

    private static FileWriteNode CreateFileWriteNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<FileWriteOptions>();
        var clock = context.GetResource<TimeProvider>(
            FileSystemComponentDefinition.Resources.Clock);
        return new FileWriteNode(options, clock);
    }

    private static DirectoryEnumerateNode CreateDirectoryEnumerateNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<DirectoryEnumerateOptions>();
        var clock = context.GetResource<TimeProvider>(
            FileSystemComponentDefinition.Resources.Clock);
        return new DirectoryEnumerateNode(options, clock);
    }

    private static FileWatchNode CreateFileWatchNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<FileWatchOptions>();
        var clock = context.GetResource<TimeProvider>(
            FileSystemComponentDefinition.Resources.Clock);
        return new FileWatchNode(options, clock);
    }
}
