namespace FluxFlow.Components.Designer.Persistence;

public sealed record DesignerResourceNamespace : DesignerResourceNode
{
    public required string Path { get; init; }

    public IReadOnlyDictionary<string, DesignerResourceNode> Entries { get; init; } =
        new Dictionary<string, DesignerResourceNode>(StringComparer.Ordinal);
}
