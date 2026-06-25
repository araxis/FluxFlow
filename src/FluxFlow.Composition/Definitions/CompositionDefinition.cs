namespace FluxFlow.Composition;

/// <summary>
/// Root DTO for a composition document. Fluent builders and configuration loading both
/// produce this model before validation and linking.
/// </summary>
public sealed record CompositionDefinition
{
    private Dictionary<string, WorkflowDefinition> _workflows = new(StringComparer.Ordinal);

    public Dictionary<string, WorkflowDefinition> Workflows
    {
        get => _workflows;
        init => _workflows = value is null
            ? new Dictionary<string, WorkflowDefinition>(StringComparer.Ordinal)
            : new Dictionary<string, WorkflowDefinition>(value, StringComparer.Ordinal);
    }
}
