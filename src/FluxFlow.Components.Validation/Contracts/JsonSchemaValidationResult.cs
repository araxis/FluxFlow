using System.Text.Json;

namespace FluxFlow.Components.Validation.Contracts;

/// <summary>
/// Outcome of evaluating a selected JSON value against a JSON Schema.
/// </summary>
public sealed record JsonSchemaValidationResult
{
    public required DateTimeOffset Timestamp { get; init; }
    public required JsonElement Input { get; init; }
    public required JsonElement Value { get; init; }
    public required bool IsValid { get; init; }
    public string? SchemaId { get; init; }
    public required string ValueSelector { get; init; }
    public IReadOnlyList<JsonSchemaValidationIssue> Issues { get; init; } = [];
}
