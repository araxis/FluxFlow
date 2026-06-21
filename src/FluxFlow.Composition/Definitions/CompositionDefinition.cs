namespace FluxFlow.Composition;

/// <summary>
/// Root DTO for a composition document. Fluent builders and configuration loading both
/// produce this model before validation and linking.
/// </summary>
public sealed record CompositionDefinition
{
    public Dictionary<string, WorkflowDefinition> Workflows { get; init; } =
        new(StringComparer.Ordinal);
}
