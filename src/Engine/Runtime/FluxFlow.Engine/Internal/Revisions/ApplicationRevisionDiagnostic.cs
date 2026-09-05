using FluxFlow.Composition.Addressing;

namespace FluxFlow.Engine.Internal.Revisions;

internal sealed record ApplicationRevisionDiagnostic
{
    public required ApplicationRevisionDiagnosticCode Code { get; init; }

    public required string Location { get; init; }

    public required string Message { get; init; }

    public ApplicationAddress? Resource { get; init; }
}
