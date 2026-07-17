using System.Collections.Immutable;

namespace FluxFlow.Composition.Links;

public sealed class ApplicationLinkCompilationResult
{
    internal ApplicationLinkCompilationResult(
        IEnumerable<CompiledApplicationLink> links,
        IEnumerable<ApplicationLinkDiagnostic> diagnostics)
    {
        Links = links.ToImmutableArray();
        Diagnostics = diagnostics.ToImmutableArray();
    }

    public IReadOnlyList<CompiledApplicationLink> Links { get; }

    public IReadOnlyList<ApplicationLinkDiagnostic> Diagnostics { get; }

    public bool IsValid => Diagnostics.Count == 0;
}
