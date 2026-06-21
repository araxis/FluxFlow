namespace FluxFlow.Composition;

public sealed record WorkflowDefinition
{
    public Dictionary<string, NodeDefinition> Nodes { get; init; } =
        new(StringComparer.Ordinal);

    public List<LinkDefinition> Links { get; init; } = [];
}
