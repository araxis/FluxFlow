using FluxFlow.Data;

namespace FluxFlow.Composition.Hosting.Revisions;

public enum ApplicationRevisionFailureStage
{
    Planning = 1,
    Preparation = 2,
    Activation = 3,
    EventPublication = 4,
    Drain = 5,
    Disposal = 6
}

public sealed record ApplicationRevisionFailure
{
    public required ApplicationRevisionFailureStage Stage { get; init; }

    public required FlowError Error { get; init; }
}
