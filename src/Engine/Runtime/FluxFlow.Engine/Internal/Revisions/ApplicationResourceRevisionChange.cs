using FluxFlow.Composition.Addressing;

namespace FluxFlow.Engine.Internal.Revisions;

internal sealed record ApplicationResourceRevisionChange
{
    public required ApplicationAddress Address { get; init; }

    public required ApplicationRevisionChangeKind Kind { get; init; }
}
