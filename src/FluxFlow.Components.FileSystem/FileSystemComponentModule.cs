using FluxFlow.Components.FileSystem.Nodes;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.FileSystem;

public sealed class FileSystemComponentModule : IFlowNodeModule
{
    public FileSystemComponentModule(FileSystemComponentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Registrations =
        [
            new FlowNodeRegistration(
                FileSystemComponentTypes.DirectoryEnumerate,
                context => DirectoryEnumerateNode.Create(context, options)),
            new FlowNodeRegistration(
                FileSystemComponentTypes.FileRead,
                context => FileReadNode.Create(context, options)),
            new FlowNodeRegistration(
                FileSystemComponentTypes.FileWatch,
                context => FileWatchNode.Create(context, options)),
            new FlowNodeRegistration(
                FileSystemComponentTypes.FileWrite,
                context => FileWriteNode.Create(context, options))
        ];
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}
