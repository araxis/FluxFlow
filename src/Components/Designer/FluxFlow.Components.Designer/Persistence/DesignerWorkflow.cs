namespace FluxFlow.Components.Designer.Persistence;

public sealed record DesignerWorkflow
{
    public required string Name { get; init; }

    public IReadOnlyDictionary<string, DesignerComponent> Components { get; init; } =
        new Dictionary<string, DesignerComponent>(StringComparer.Ordinal);
}
