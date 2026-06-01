using FluxFlow.Components.Validation.Options;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Validation.Contracts;

public sealed record JsonSchemaValidatorContext
{
    public required NodeAddress Address { get; init; }
    public required JsonSchemaValidatorOptions Options { get; init; }
    public required Type InputType { get; init; }
}
