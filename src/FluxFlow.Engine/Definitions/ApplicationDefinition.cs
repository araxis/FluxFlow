namespace FluxFlow.Engine.Definitions;

public sealed record ApplicationDefinition
{
    private Dictionary<string, NodeDefinition>? _resources = [];
    private Dictionary<string, WorkflowDefinition>? _workflows = [];

    public Dictionary<string, NodeDefinition> Resources
    {
        get => _resources ??= [];
        init => _resources = value ?? [];
    }

    public Dictionary<string, WorkflowDefinition> Workflows
    {
        get => _workflows ??= [];
        init => _workflows = value ?? [];
    }
}
