namespace FluxFlow.Components.Validation.Contracts;

public sealed record JsonSchemaValidationResult<TInput>
{
    public required DateTimeOffset Timestamp { get; init; }
    public required TInput Input { get; init; }
    public object? Value { get; init; }
    public required bool IsValid { get; init; }
    public string? SchemaId { get; init; }
    public required string ValueSelector { get; init; }
    public IReadOnlyList<JsonSchemaValidationIssue> Issues { get; init; } = [];
}
