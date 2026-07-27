using FluxFlow.Components.Designer;
using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Nodes;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.FileSystem.Composition;

public static class FileSystemServiceCollectionExtensions
{
    internal static ComponentDescriptor ReadDescriptor { get; } = new(
        FileSystemComponentTypes.Read,
        CreateFileReadNode,
        inputs:
        [
            ComponentPorts.Metadata<FileReadRequest>(FileSystemComponentPortNames.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<FileReadContent>(FileSystemComponentPortNames.Output)
        ]);

    internal static ComponentDescriptor WriteDescriptor { get; } = new(
        FileSystemComponentTypes.Write,
        CreateFileWriteNode,
        inputs:
        [
            ComponentPorts.Metadata<FileContentWriteRequest>(FileSystemComponentPortNames.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<FileWriteResult>(FileSystemComponentPortNames.Output)
        ]);

    internal static ComponentDescriptor DirectoryEnumerateDescriptor { get; } = new(
        FileSystemComponentTypes.DirectoryEnumerate,
        CreateDirectoryEnumerateNode,
        outputs:
        [
            ComponentPorts.Metadata<DirectoryEntry>(FileSystemComponentPortNames.Output)
        ]);

    internal static ComponentDescriptor WatchDescriptor { get; } = new(
        FileSystemComponentTypes.Watch,
        CreateFileWatchNode,
        outputs:
        [
            ComponentPorts.Metadata<FileChange>(FileSystemComponentPortNames.Output)
        ]);

    public static IServiceCollection AddFileSystemComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddFluxFlowComponent(ReadDescriptor);
        services.AddFluxFlowComponent(WriteDescriptor);
        services.AddFluxFlowComponent(DirectoryEnumerateDescriptor);
        services.AddFluxFlowComponent(WatchDescriptor);
        services.AddComponentDesignMetadataProvider<FileSystemComponentDesignMetadataProvider>();
        return services;
    }

    private static ValueTask<ComponentInstance> CreateFileReadNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<FileReadOptions>();
        var clock = context.GetResource<TimeProvider>(
            FileSystemComponentResourceNames.Clock);
        var node = new FileReadNode(options, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<FileReadRequest>(
                    FileSystemComponentPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<FileReadContent>(
                    FileSystemComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComponentInstance> CreateFileWriteNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<FileWriteOptions>();
        var clock = context.GetResource<TimeProvider>(
            FileSystemComponentResourceNames.Clock);
        var node = new FileWriteNode(options, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<FileContentWriteRequest>(
                    FileSystemComponentPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<FileWriteResult>(
                    FileSystemComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComponentInstance> CreateDirectoryEnumerateNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<DirectoryEnumerateOptions>();
        var clock = context.GetResource<TimeProvider>(
            FileSystemComponentResourceNames.Clock);
        var node = new DirectoryEnumerateNode(options, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            outputs:
            [
                ComponentPorts.Output<DirectoryEntry>(
                    FileSystemComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComponentInstance> CreateFileWatchNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<FileWatchOptions>();
        var clock = context.GetResource<TimeProvider>(
            FileSystemComponentResourceNames.Clock);
        var node = new FileWatchNode(options, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            outputs:
            [
                ComponentPorts.Output<FileChange>(
                    FileSystemComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }
}
