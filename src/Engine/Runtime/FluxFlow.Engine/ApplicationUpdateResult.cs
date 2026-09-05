using FluxFlow.Data;

namespace FluxFlow.Engine;

public enum ApplicationUpdateStatus
{
    Applied = 1,
    Unchanged = 2,
    Rejected = 3
}

public enum ApplicationUpdateStage
{
    Source = 1,
    Validation = 2,
    Planning = 3,
    ResourcePreparation = 4,
    ComponentPreparation = 5,
    Activation = 6,
    Swap = 7,
    Drain = 8,
    Disposal = 9,
    EventPublication = 10
}

public sealed record ApplicationUpdateDiagnostic
{
    public required ApplicationUpdateStage Stage { get; init; }

    public required FlowError Error { get; init; }
}

public sealed record ApplicationUpdateResult
{
    public required ApplicationUpdateStatus Status { get; init; }

    public required string RequestedRevisionId { get; init; }

    public ApplicationSnapshot? ActiveRevision { get; init; }

    public ApplicationSnapshot? PreviousRevision { get; init; }

    public IReadOnlyList<ApplicationUpdateDiagnostic> Diagnostics { get; init; } = [];

    public bool HasChanges { get; init; }

    public bool IsApplied => Status == ApplicationUpdateStatus.Applied;

    public bool IsRejected => Status == ApplicationUpdateStatus.Rejected;
}
