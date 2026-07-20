using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Nodes;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.FileSystem.Composition;

public static class FileSystemTypedRegistrationExtensions
{
    public static CompositionNodeRegistry RegisterFileReadResult(
        this CompositionNodeRegistry registry,
        string nodeType)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateFileReadResultNode,
            inputs:
            [
                CompositionPorts.Metadata<FileReadRequest>(
                    FileSystemCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FileReadResult>(
                    FileSystemCompositionPortNames.Output)
            ]);
    }

    public static CompositionNodeRegistry RegisterFileWriteResult(
        this CompositionNodeRegistry registry,
        string nodeType)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateFileWriteResultNode,
            inputs:
            [
                CompositionPorts.Metadata<FileWriteRequest>(
                    FileSystemCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FileWriteResult>(
                    FileSystemCompositionPortNames.Output)
            ]);
    }

    public static CompositionNodeRegistry RegisterDirectoryEnumerateEntries(
        this CompositionNodeRegistry registry,
        string nodeType)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateDirectoryEnumerateEntryNode,
            outputs:
            [
                CompositionPorts.Metadata<DirectoryEnumerateEntry>(
                    FileSystemCompositionPortNames.Output)
            ]);
    }

    public static CompositionNodeRegistry RegisterFileWatchEvents(
        this CompositionNodeRegistry registry,
        string nodeType)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateFileWatchEventNode,
            outputs:
            [
                CompositionPorts.Metadata<FileWatchEvent>(
                    FileSystemCompositionPortNames.Output)
            ]);
    }

    private static ValueTask<ComposedNode> CreateFileReadResultNode(
        CompositionNodeFactoryContext context)
    {
        var node = new FileReadNode(
            context.BindConfiguration<FileReadOptions>(),
            GetClock(context));
        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<FileReadRequest>(
                    FileSystemCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<FileReadResult>(
                    FileSystemCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            errors: node.Errors));
    }

    private static ValueTask<ComposedNode> CreateFileWriteResultNode(
        CompositionNodeFactoryContext context)
    {
        var node = new FileWriteNode(
            context.BindConfiguration<FileWriteOptions>(),
            GetClock(context));
        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<FileWriteRequest>(
                    FileSystemCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<FileWriteResult>(
                    FileSystemCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            errors: node.Errors));
    }

    private static ValueTask<ComposedNode> CreateDirectoryEnumerateEntryNode(
        CompositionNodeFactoryContext context)
    {
        var node = new DirectoryEnumerateNode(
            context.BindConfiguration<DirectoryEnumerateOptions>(),
            GetClock(context));
        return ValueTask.FromResult(ComposedNode.Create(
            node,
            outputs:
            [
                CompositionPorts.Output<DirectoryEnumerateEntry>(
                    FileSystemCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            errors: node.Errors));
    }

    private static ValueTask<ComposedNode> CreateFileWatchEventNode(
        CompositionNodeFactoryContext context)
    {
        var node = new FileWatchNode(
            context.BindConfiguration<FileWatchOptions>(),
            GetClock(context));
        return ValueTask.FromResult(ComposedNode.Create(
            node,
            outputs:
            [
                CompositionPorts.Output<FileWatchEvent>(
                    FileSystemCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            errors: node.Errors));
    }

    private static TimeProvider? GetClock(CompositionNodeFactoryContext context)
        => context.GetResource<TimeProvider>(FileSystemCompositionResourceNames.Clock);
}
