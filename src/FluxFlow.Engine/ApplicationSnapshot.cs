using FluxFlow.Composition.Model;

namespace FluxFlow.Engine;

public sealed record ApplicationSnapshot
{
    public required long Sequence { get; init; }

    public required string RevisionId { get; init; }

    public required DateTimeOffset ActivatedAt { get; init; }

    public required ApplicationDefinition Definition { get; init; }
}
