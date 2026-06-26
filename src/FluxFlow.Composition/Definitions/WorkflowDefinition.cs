namespace FluxFlow.Composition;

public sealed record WorkflowDefinition
{
    private Dictionary<string, NodeDefinition> _nodes = new(StringComparer.Ordinal);
    private List<LinkDefinition> _links = [];

    public Dictionary<string, NodeDefinition> Nodes
    {
        get => _nodes;
        init => _nodes = CompositionDictionary.NormalizeKeys(value, nameof(Nodes));
    }

    public List<LinkDefinition> Links
    {
        get => _links;
        init => _links = value is null ? [] : [.. value];
    }
}
