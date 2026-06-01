using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Observability.Contracts;

public sealed record ObservabilityNodeContext
{
    public required NodeAddress Address { get; init; }
    public required string NodeType { get; init; }
    public required Type InputType { get; init; }
    public required string Name { get; init; }
}
