using FluxFlow.Components.Mapping.Options;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Mapping.Contracts;

public sealed record MappingNodeContext
{
    public required NodeAddress Address { get; init; }
    public required MapperOptions Options { get; init; }
    public required Type InputType { get; init; }
    public required Type OutputType { get; init; }
}
