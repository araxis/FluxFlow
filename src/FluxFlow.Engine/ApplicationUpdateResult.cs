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
    Normalization = 2,
    Validation = 3,
    Planning = 4,
    ResourcePreparation = 5,
    ComponentPreparation = 6,
    Activation = 7,
    Swap = 8,
    Drain = 9,
    Disposal = 10,
    EventPublication = 11
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
