using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Nodes;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.FileSystem.Composition;

public static class FileSystemServiceCollectionExtensions
{
    public static IServiceCollection AddFileSystem(this IServiceCollection builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddComponent(FileSystemComponentDefinition.Types.Read, FileSystemServiceCollectionExtensions.CreateFileReadNode)
            .AddComponent(FileSystemComponentDefinition.Types.Write, FileSystemServiceCollectionExtensions.CreateFileWriteNode)
            .AddComponent(FileSystemComponentDefinition.Types.DirectoryEnumerate, FileSystemServiceCollectionExtensions.CreateDirectoryEnumerateNode)
            .AddComponent(FileSystemComponentDefinition.Types.Watch, FileSystemServiceCollectionExtensions.CreateFileWatchNode);
    }

    internal static ValueTask<IFlowNode> CreateFileReadNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<FileReadOptions>();
        var clock = context.GetResource<TimeProvider>(
            FileSystemComponentDefinition.Resources.Clock);
        var node = new FileReadNode(options, clock);
        context.HasInput(node.Input);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return ValueTask.FromResult<IFlowNode>(node);
    }

    internal static ValueTask<IFlowNode> CreateFileWriteNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<FileWriteOptions>();
        var clock = context.GetResource<TimeProvider>(
            FileSystemComponentDefinition.Resources.Clock);
        var node = new FileWriteNode(options, clock);
        context.HasInput(node.Input);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return ValueTask.FromResult<IFlowNode>(node);
    }

    internal static ValueTask<IFlowNode> CreateDirectoryEnumerateNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<DirectoryEnumerateOptions>();
        var clock = context.GetResource<TimeProvider>(
            FileSystemComponentDefinition.Resources.Clock);
        var node = new DirectoryEnumerateNode(options, clock);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return ValueTask.FromResult<IFlowNode>(node);
    }

    internal static ValueTask<IFlowNode> CreateFileWatchNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<FileWatchOptions>();
        var clock = context.GetResource<TimeProvider>(
            FileSystemComponentDefinition.Resources.Clock);
        var node = new FileWatchNode(options, clock);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return ValueTask.FromResult<IFlowNode>(node);
    }
}
