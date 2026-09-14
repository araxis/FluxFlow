using FluxFlow.Json.Links;
using System.Collections.Immutable;
using FluxFlow.Core.Addressing;

namespace FluxFlow.Composition.Links;

public sealed class ApplicationLinkCompilationResult
{
    internal ApplicationLinkCompilationResult(
        IEnumerable<CompiledApplicationLink> links,
        IEnumerable<ApplicationLinkDeclarationProjection> declarations,
        IEnumerable<ApplicationLinkDiagnostic> diagnostics,
        IEnumerable<ApplicationAddress>? discardedOutputs = null)
    {
        Links = links.ToImmutableArray();
        Declarations = declarations.ToImmutableArray();
        Diagnostics = diagnostics.ToImmutableArray();
        DiscardedOutputs = (discardedOutputs ?? []).ToImmutableArray();
    }

    public IReadOnlyList<CompiledApplicationLink> Links { get; }

    public IReadOnlyList<ApplicationLinkDeclarationProjection> Declarations { get; }

    public IReadOnlyList<ApplicationLinkDiagnostic> Diagnostics { get; }

    public IReadOnlyList<ApplicationAddress> DiscardedOutputs { get; }


    public bool IsValid => Diagnostics.Count == 0;
}
