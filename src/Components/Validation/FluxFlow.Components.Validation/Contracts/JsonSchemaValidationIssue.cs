namespace FluxFlow.Components.Validation.Contracts;

public sealed record JsonSchemaValidationIssue
{
    public string? Keyword { get; init; }
    public string? Message { get; init; }
    public string? EvaluationPath { get; init; }
    public string? InstanceLocation { get; init; }
    public string? SchemaLocation { get; init; }
}
