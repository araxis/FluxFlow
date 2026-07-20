namespace FluxFlow.Components.Designer.Persistence;

public sealed record DesignerApplicationDocument
{
    public required DesignerResourceNamespace Resources { get; init; }

    public IReadOnlyDictionary<string, DesignerWorkflow> Workflows { get; init; } =
        new Dictionary<string, DesignerWorkflow>(StringComparer.Ordinal);

    public IReadOnlyList<DesignerApplicationLink> Links { get; init; } = [];

    public IReadOnlyList<DesignerResourceReference> ResourceReferences { get; init; } = [];
}
