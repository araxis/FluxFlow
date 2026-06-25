namespace FluxFlow.Composition;

public sealed record WorkflowDefinition
{
    private Dictionary<string, NodeDefinition> _nodes = new(StringComparer.Ordinal);
    private List<LinkDefinition> _links = [];

    public Dictionary<string, NodeDefinition> Nodes
    {
        get => _nodes;
        init => _nodes = value is null
            ? new Dictionary<string, NodeDefinition>(StringComparer.Ordinal)
            : new Dictionary<string, NodeDefinition>(value, StringComparer.Ordinal);
    }

    public List<LinkDefinition> Links
    {
        get => _links;
        init => _links = value is null ? [] : [.. value];
    }
}
