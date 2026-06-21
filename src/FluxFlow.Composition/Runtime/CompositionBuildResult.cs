namespace FluxFlow.Composition;

public sealed record CompositionBuildResult(
    CompositionRuntime? Runtime,
    IReadOnlyList<CompositionDiagnostic> Diagnostics)
{
    public bool Succeeded => Runtime is not null && Diagnostics.Count == 0;

    public static CompositionBuildResult Success(CompositionRuntime runtime)
        => new(runtime, []);

    public static CompositionBuildResult Failure(IEnumerable<CompositionDiagnostic> diagnostics)
        => new(null, diagnostics.ToArray());
}
