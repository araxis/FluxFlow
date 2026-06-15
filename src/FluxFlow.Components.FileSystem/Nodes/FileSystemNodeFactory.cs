using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.FileSystem.Nodes;

internal static class FileSystemNodeFactory
{
    public static RuntimeNode CreateRead(RuntimeNodeFactoryContext context)
        => CreateRead(context, new FileSystemComponentOptions());

    public static RuntimeNode CreateRead(
        RuntimeNodeFactoryContext context,
        FileSystemComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = FileSystemOptionsReader.ReadFileReadOptions(context.Definition);
        var node = new FileReadNode(options, componentOptions.Clock);

        return context.CreateNode(node)
            .Input(FileSystemComponentPorts.Input, node.Input)
            .Output(FileSystemComponentPorts.Result, node.Result)
            .Build();
    }

    public static RuntimeNode CreateWrite(RuntimeNodeFactoryContext context)
        => CreateWrite(context, new FileSystemComponentOptions());

    public static RuntimeNode CreateWrite(
        RuntimeNodeFactoryContext context,
        FileSystemComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = FileSystemOptionsReader.ReadFileWriteOptions(context.Definition);
        var node = new FileWriteNode(options, componentOptions.Clock);

        return context.CreateNode(node)
            .Input(FileSystemComponentPorts.Input, node.Input)
            .Output(FileSystemComponentPorts.Result, node.Result)
            .Build();
    }

    public static RuntimeNode CreateWatch(RuntimeNodeFactoryContext context)
        => CreateWatch(context, new FileSystemComponentOptions());

    public static RuntimeNode CreateWatch(
        RuntimeNodeFactoryContext context,
        FileSystemComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = FileSystemOptionsReader.ReadFileWatchOptions(context.Definition);
        var node = new FileWatchNode(options, componentOptions.Clock);

        return context.CreateNode(node)
            .Output(FileSystemComponentPorts.Output, node.Output)
            .Build();
    }

    public static RuntimeNode CreateEnumerate(RuntimeNodeFactoryContext context)
        => CreateEnumerate(context, new FileSystemComponentOptions());

    public static RuntimeNode CreateEnumerate(
        RuntimeNodeFactoryContext context,
        FileSystemComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = FileSystemOptionsReader.ReadDirectoryEnumerateOptions(context.Definition);
        var node = new DirectoryEnumerateNode(options, componentOptions.Clock);

        return context.CreateNode(node)
            .Output(FileSystemComponentPorts.Output, node.Output)
            .Build();
    }
}
