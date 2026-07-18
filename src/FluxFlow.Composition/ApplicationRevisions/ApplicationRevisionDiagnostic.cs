using FluxFlow.Composition.Addressing;

namespace FluxFlow.Composition.Revisions;

public sealed record ApplicationRevisionDiagnostic
{
    public required ApplicationRevisionDiagnosticCode Code { get; init; }

    public required string Location { get; init; }

    public required string Message { get; init; }

    public ApplicationAddress? Resource { get; init; }
}
