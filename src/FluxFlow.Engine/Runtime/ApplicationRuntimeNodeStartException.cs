using FluxFlow.Engine.Definitions;

namespace FluxFlow.Engine.Runtime;

public sealed class ApplicationRuntimeNodeStartException(NodeAddress nodeAddress, Exception innerException)
    : Exception($"Node '{nodeAddress}' failed to start: {innerException.Message}", innerException)
{
    public NodeAddress NodeAddress { get; } = nodeAddress;
}
