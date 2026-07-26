using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Nodes;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.FileSystem.Composition;

public static class FileSystemCompositionNodeRegistryExtensions
{
    public static CompositionNodeRegistry RegisterFileRead(
        this CompositionNodeRegistry registry,
        string nodeType = FileSystemCompositionNodeTypes.Read)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateFileReadNode,
            inputs:
            [
                CompositionPorts.Metadata<FileReadRequest>(
                    FileSystemCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FileReadContent>(
                    FileSystemCompositionPortNames.Output)
            ]);
    }

    public static CompositionNodeRegistry RegisterFileWrite(
        this CompositionNodeRegistry registry,
        string nodeType = FileSystemCompositionNodeTypes.Write)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateFileWriteNode,
            inputs:
            [
                CompositionPorts.Metadata<FileContentWriteRequest>(
                    FileSystemCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FileWriteResult>(
                    FileSystemCompositionPortNames.Output)
            ]);
    }

    public static CompositionNodeRegistry RegisterDirectoryEnumerate(
        this CompositionNodeRegistry registry,
        string nodeType = FileSystemCompositionNodeTypes.DirectoryEnumerate)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            FileSystemCompositionNodeTypes.DirectoryEnumerateDescriptor,
            CreateDirectoryEnumerateNode,
            outputs:
            [
                CompositionPorts.Metadata<DirectoryEntry>(
                    FileSystemCompositionPortNames.Output)
            ],
            registrationType: nodeType);
    }

    public static CompositionNodeRegistry RegisterFileWatch(
        this CompositionNodeRegistry registry,
        string nodeType = FileSystemCompositionNodeTypes.Watch)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateFileWatchNode,
            outputs:
            [
                CompositionPorts.Metadata<FileChange>(
                    FileSystemCompositionPortNames.Output)
            ]);
    }

    private static ValueTask<ComposedNode> CreateFileReadNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<FileReadOptions>();
        var clock = context.GetResource<TimeProvider>(
            FileSystemCompositionResourceNames.Clock);
        var node = new FileReadNode(options, clock);

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
                CompositionPorts.Output<FileReadContent>(
                    FileSystemCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComposedNode> CreateFileWriteNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<FileWriteOptions>();
        var clock = context.GetResource<TimeProvider>(
            FileSystemCompositionResourceNames.Clock);
        var node = new FileWriteNode(options, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<FileContentWriteRequest>(
                    FileSystemCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<FileWriteResult>(
                    FileSystemCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComposedNode> CreateDirectoryEnumerateNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<DirectoryEnumerateOptions>();
        var clock = context.GetResource<TimeProvider>(
            FileSystemCompositionResourceNames.Clock);
        var node = new DirectoryEnumerateNode(options, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            outputs:
            [
                CompositionPorts.Output<DirectoryEntry>(
                    FileSystemCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static ValueTask<ComposedNode> CreateFileWatchNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<FileWatchOptions>();
        var clock = context.GetResource<TimeProvider>(
            FileSystemCompositionResourceNames.Clock);
        var node = new FileWatchNode(options, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            outputs:
            [
                CompositionPorts.Output<FileChange>(
                    FileSystemCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }
}
