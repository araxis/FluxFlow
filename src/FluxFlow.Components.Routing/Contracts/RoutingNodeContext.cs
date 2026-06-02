using FluxFlow.Components.Routing.Options;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Routing.Contracts;

public sealed record RoutingNodeContext
{
    public required NodeAddress Address { get; init; }
    public required SwitchRoutingOptions Options { get; init; }
    public required Type InputType { get; init; }
}
