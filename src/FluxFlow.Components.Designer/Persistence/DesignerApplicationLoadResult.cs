using FluxFlow.Composition.Links;
using FluxFlow.Composition.Model;

namespace FluxFlow.Components.Designer.Persistence;

public sealed record DesignerApplicationLoadResult
{
    public required DesignerApplicationDocument Document { get; init; }

    public IReadOnlyList<ApplicationLinkDiagnostic> Diagnostics { get; init; } = [];

    public IReadOnlyList<ApplicationDefinitionNormalizationDiagnostic> NormalizationDiagnostics { get; init; } = [];

    public bool IsValid => Diagnostics.Count == 0;
}
