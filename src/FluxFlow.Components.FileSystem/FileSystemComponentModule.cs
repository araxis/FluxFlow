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
                FileSystemComponentTypes.FileRead,
                FileReadNode.Create),
            new FlowNodeRegistration(
                FileSystemComponentTypes.FileWrite,
                FileWriteNode.Create)
        ];
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}
