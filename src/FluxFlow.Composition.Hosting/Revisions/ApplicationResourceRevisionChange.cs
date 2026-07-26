using FluxFlow.Composition.Addressing;

namespace FluxFlow.Composition.Hosting.Revisions;

public sealed record ApplicationResourceRevisionChange
{
    public required ApplicationAddress Address { get; init; }

    public required ApplicationRevisionChangeKind Kind { get; init; }
}
