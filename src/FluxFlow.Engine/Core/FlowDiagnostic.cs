namespace FluxFlow.Engine.Components;

public sealed record FlowDiagnostic
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string Name { get; init; }
    public FlowDiagnosticLevel Level { get; init; } = FlowDiagnosticLevel.Information;
    public string? Message { get; init; }
    public Exception? Exception { get; init; }
    public IReadOnlyDictionary<string, object?> Attributes { get; init; } = EmptyAttributes;

    private static readonly IReadOnlyDictionary<string, object?> EmptyAttributes =
        new Dictionary<string, object?>(StringComparer.Ordinal);
}
