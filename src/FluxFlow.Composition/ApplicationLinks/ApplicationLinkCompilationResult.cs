using System.Collections.Immutable;

namespace FluxFlow.Composition.Links;

public sealed class ApplicationLinkCompilationResult
{
    internal ApplicationLinkCompilationResult(
        IEnumerable<CompiledApplicationLink> links,
        IEnumerable<ApplicationLinkDeclarationProjection> declarations,
        IEnumerable<ApplicationLinkDiagnostic> diagnostics)
    {
        Links = links.ToImmutableArray();
        Declarations = declarations.ToImmutableArray();
        Diagnostics = diagnostics.ToImmutableArray();
    }

    public IReadOnlyList<CompiledApplicationLink> Links { get; }

    public IReadOnlyList<ApplicationLinkDeclarationProjection> Declarations { get; }

    public IReadOnlyList<ApplicationLinkDiagnostic> Diagnostics { get; }

    public bool IsValid => Diagnostics.Count == 0;
}
