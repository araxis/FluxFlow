using FluxFlow.Data;

namespace FluxFlow.Components.Validation.Contracts;

/// <summary>
/// Transport-neutral outcome of evaluating a selected <see cref="FlowValue"/>
/// against a JSON Schema.
/// </summary>
public sealed record JsonSchemaFlowValueValidationResult
{
    public required DateTimeOffset Timestamp { get; init; }

    public required FlowValue Input { get; init; }

    public required FlowValue Value { get; init; }

    public required bool IsValid { get; init; }

    public string? SchemaId { get; init; }

    public required string ValueSelector { get; init; }

    public IReadOnlyList<JsonSchemaValidationIssue> Issues { get; init; } = [];
}
