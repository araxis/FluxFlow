namespace FluxFlow.Components.Validation.Nodes;

internal sealed record JsonSchemaValidatorMetadata
{
    public required string InputType { get; init; }
    public required string ValueSelector { get; init; }
    public string? SchemaId { get; init; }
    public string? SchemaPath { get; init; }
}
