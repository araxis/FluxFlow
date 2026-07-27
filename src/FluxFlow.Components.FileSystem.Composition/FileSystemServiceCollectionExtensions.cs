using FluxFlow.Components.Designer;
using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Nodes;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.FileSystem.Composition;

public static class FileSystemServiceCollectionExtensions
{
    private static readonly Lazy<IReadOnlyCollection<ComponentDesignDeclaration>> DeclarationSet =
        new(CreateDeclarations);

    internal static IReadOnlyCollection<ComponentDesignDeclaration> Declarations =>
        DeclarationSet.Value;

    private static IReadOnlyCollection<ComponentDesignDeclaration> CreateDeclarations() =>
        ComponentDesignDeclaration.CreateRange(
            [
                ReadDescriptor,
                WriteDescriptor,
                DirectoryEnumerateDescriptor,
                WatchDescriptor
            ],
            FileSystemComponentDefinition.CreateMetadata());

    internal static ComponentDescriptor ReadDescriptor { get; } = new(
        FileSystemComponentDefinition.Types.Read,
        CreateFileReadNode,
        inputs:
        [
            ComponentPorts.Metadata<FileReadRequest>(FileSystemComponentDefinition.Ports.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<FileReadContent>(FileSystemComponentDefinition.Ports.Output)
        ],
        options: FileSystemComponentDefinition.CreateOptions(FileSystemComponentDefinition.Types.Read),
        resources: FileSystemComponentDefinition.CreateResources(FileSystemComponentDefinition.Types.Read));

    internal static ComponentDescriptor WriteDescriptor { get; } = new(
        FileSystemComponentDefinition.Types.Write,
        CreateFileWriteNode,
        inputs:
        [
            ComponentPorts.Metadata<FileContentWriteRequest>(FileSystemComponentDefinition.Ports.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<FileWriteResult>(FileSystemComponentDefinition.Ports.Output)
        ],
        options: FileSystemComponentDefinition.CreateOptions(FileSystemComponentDefinition.Types.Write),
        resources: FileSystemComponentDefinition.CreateResources(FileSystemComponentDefinition.Types.Write));

    internal static ComponentDescriptor DirectoryEnumerateDescriptor { get; } = new(
        FileSystemComponentDefinition.Types.DirectoryEnumerate,
        CreateDirectoryEnumerateNode,
        outputs:
        [
            ComponentPorts.Metadata<DirectoryEntry>(FileSystemComponentDefinition.Ports.Output)
        ],
        options: FileSystemComponentDefinition.CreateOptions(FileSystemComponentDefinition.Types.DirectoryEnumerate),
        resources: FileSystemComponentDefinition.CreateResources(FileSystemComponentDefinition.Types.DirectoryEnumerate));

    internal static ComponentDescriptor WatchDescriptor { get; } = new(
        FileSystemComponentDefinition.Types.Watch,
        CreateFileWatchNode,
        outputs:
        [
            ComponentPorts.Metadata<FileChange>(FileSystemComponentDefinition.Ports.Output)
        ],
        options: FileSystemComponentDefinition.CreateOptions(FileSystemComponentDefinition.Types.Watch),
        resources: FileSystemComponentDefinition.CreateResources(FileSystemComponentDefinition.Types.Watch));

    public static IServiceCollection AddFileSystemComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddComponentDesignDeclarations(Declarations);
        return services;
    }

    private static ValueTask<ComponentInstance> CreateFileReadNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<FileReadOptions>();
        var clock = context.GetResource<TimeProvider>(
            FileSystemComponentDefinition.Resources.Clock);
        var node = new FileReadNode(options, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<FileReadRequest>(
                    FileSystemComponentDefinition.Ports.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<FileReadContent>(
                    FileSystemComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComponentInstance> CreateFileWriteNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<FileWriteOptions>();
        var clock = context.GetResource<TimeProvider>(
            FileSystemComponentDefinition.Resources.Clock);
        var node = new FileWriteNode(options, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<FileContentWriteRequest>(
                    FileSystemComponentDefinition.Ports.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<FileWriteResult>(
                    FileSystemComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComponentInstance> CreateDirectoryEnumerateNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<DirectoryEnumerateOptions>();
        var clock = context.GetResource<TimeProvider>(
            FileSystemComponentDefinition.Resources.Clock);
        var node = new DirectoryEnumerateNode(options, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            outputs:
            [
                ComponentPorts.Output<DirectoryEntry>(
                    FileSystemComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComponentInstance> CreateFileWatchNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<FileWatchOptions>();
        var clock = context.GetResource<TimeProvider>(
            FileSystemComponentDefinition.Resources.Clock);
        var node = new FileWatchNode(options, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            outputs:
            [
                ComponentPorts.Output<FileChange>(
                    FileSystemComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }
}
