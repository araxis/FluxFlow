namespace FluxFlow.Engine.Definitions;

public sealed record WorkflowDefinition
{
    private Dictionary<string, NodeDefinition>? _nodes = [];

    public Dictionary<string, NodeDefinition> Nodes
    {
        get => _nodes ??= [];
        init => _nodes = value ?? [];
    }
}
