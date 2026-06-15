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
                context => FileSystemNodeFactory.CreateEnumerate(context, options)),
            new FlowNodeRegistration(
                FileSystemComponentTypes.FileRead,
                context => FileSystemNodeFactory.CreateRead(context, options)),
            new FlowNodeRegistration(
                FileSystemComponentTypes.FileWatch,
                context => FileSystemNodeFactory.CreateWatch(context, options)),
            new FlowNodeRegistration(
                FileSystemComponentTypes.FileWrite,
                context => FileSystemNodeFactory.CreateWrite(context, options))
        ];
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}
