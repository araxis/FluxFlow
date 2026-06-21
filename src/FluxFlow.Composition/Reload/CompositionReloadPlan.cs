namespace FluxFlow.Composition;

public sealed record CompositionReloadPlan(
    CompositionReloadAction Action,
    IReadOnlyList<CompositionDiagnostic> Diagnostics)
{
    public static CompositionReloadPlan NoChange { get; } =
        new(CompositionReloadAction.NoChange, []);

    public static CompositionReloadPlan Restart { get; } =
        new(CompositionReloadAction.Restart, []);
}
