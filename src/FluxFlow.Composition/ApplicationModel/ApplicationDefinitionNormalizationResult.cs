namespace FluxFlow.Composition.Model;

public sealed record ApplicationDefinitionNormalizationResult
{
    public required ApplicationDefinition Definition { get; init; }

    public IReadOnlyList<ApplicationDefinitionNormalizationDiagnostic> Diagnostics { get; init; } = [];

    public bool Changed => Diagnostics.Count != 0;
}
