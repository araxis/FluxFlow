namespace FluxFlow.Components.Designer.Persistence;

public sealed record DesignerWorkflow
{
    public string? ExpressionEngine { get; init; }
    public required string Name { get; init; }

    public IReadOnlyDictionary<string, DesignerComponent> Components { get; init; } =
        new Dictionary<string, DesignerComponent>(StringComparer.Ordinal);
}
