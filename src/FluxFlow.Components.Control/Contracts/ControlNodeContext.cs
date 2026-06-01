using FluxFlow.Components.Control.Options;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Control.Contracts;

public sealed record ControlNodeContext
{
    public required NodeAddress Address { get; init; }
    public required ControlExpressionOptions Options { get; init; }
    public required Type InputType { get; init; }
}
