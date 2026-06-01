using FluxFlow.Components.Assertions.Options;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Assertions.Contracts;

public sealed record AssertionNodeContext
{
    public required NodeAddress Address { get; init; }
    public required AssertionOptions Options { get; init; }
    public required Type InputType { get; init; }
}
