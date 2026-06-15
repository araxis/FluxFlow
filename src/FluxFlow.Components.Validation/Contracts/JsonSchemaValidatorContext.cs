using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Validation.Contracts;

public sealed record JsonSchemaValidatorContext
{
    public required NodeAddress Address { get; init; }
    public required Type InputType { get; init; }
    public string? ValueSelector { get; init; }
}
