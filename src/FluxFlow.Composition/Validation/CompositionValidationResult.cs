namespace FluxFlow.Composition;

public sealed record CompositionValidationResult(IReadOnlyList<CompositionDiagnostic> Diagnostics)
{
    public bool IsValid => Diagnostics.Count == 0;

    public static CompositionValidationResult Success { get; } = new([]);
}
