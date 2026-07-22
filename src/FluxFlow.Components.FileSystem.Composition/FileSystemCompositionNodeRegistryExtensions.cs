using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Nodes;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Composition;
using FluxFlow.Data;

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
                CompositionPorts.Metadata<FlowResult<FileReadContent>>(
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
                CompositionPorts.Metadata<FlowResult<FileWriteResult>>(
                    FileSystemCompositionPortNames.Output)
            ]);
    }

    public static CompositionNodeRegistry RegisterDirectoryEnumerate(
        this CompositionNodeRegistry registry,
        string nodeType = FileSystemCompositionNodeTypes.DirectoryEnumerate)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        var result = registry.Register(
            nodeType,
            CreateDirectoryEnumerateNode,
            outputs:
            [
                CompositionPorts.Metadata<FlowValue>(
                    FileSystemCompositionPortNames.Output)
            ]);

        if (string.Equals(nodeType, FileSystemCompositionNodeTypes.DirectoryEnumerate, StringComparison.Ordinal))
        {
            result.RegisterAlias(
                FileSystemCompositionNodeTypes.LegacyDirectoryEnumerate,
                FileSystemCompositionNodeTypes.DirectoryEnumerate);
        }

        return result;
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
                CompositionPorts.Metadata<FlowValue>(
                    FileSystemCompositionPortNames.Output)
            ]);
    }

    private static ValueTask<ComposedNode> CreateFileReadNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<FileReadOptions>();
        var clock = context.GetResource<TimeProvider>(
            FileSystemCompositionResourceNames.Clock);
        var node = new FlowContentFileReadNode(options, clock);

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
                CompositionPorts.Output<FlowResult<FileReadContent>>(
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
        var node = new FlowContentFileWriteNode(options, clock);

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
                CompositionPorts.Output<FlowResult<FileWriteResult>>(
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
        var node = new FlowValueDirectoryEnumerateNode(options, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            outputs:
            [
                CompositionPorts.Output<FlowValue>(
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
        var node = new FlowValueFileWatchNode(options, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            outputs:
            [
                CompositionPorts.Output<FlowValue>(
                    FileSystemCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }
}
