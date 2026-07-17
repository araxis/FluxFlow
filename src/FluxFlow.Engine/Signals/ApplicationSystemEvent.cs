using FluxFlow.Data;

namespace FluxFlow.Engine.Signals;

public enum ApplicationSystemEventCategory
{
    Lifecycle = 1,
    Component = 2,
    Link = 3,
    Resource = 4,
    Revision = 5
}

public static class ApplicationSystemEventNames
{
    public const string RuntimeCompleting = "flow.runtime.completing";
    public const string ComponentFaulted = "flow.component.faulted";
    public const string LinkConditionFailed = "flow.link.condition.failed";
    public const string LinkTargetRejected = "flow.link.target.rejected";
    public const string ResourceChanged = "flow.resource.changed";
    public const string RevisionChanged = "flow.revision.changed";
}

public sealed record ApplicationSystemEvent
{
    public required DateTimeOffset Timestamp { get; init; }

    public required string Name { get; init; }

    public required ApplicationSystemEventCategory Category { get; init; }

    public string? Subject { get; init; }

    public FlowError? Error { get; init; }

    public FlowValue Details { get; init; } = FlowValue.FromObject([]);
}

public enum SystemEventPublishStatus
{
    Accepted = 1,
    Completed = 2
}

public sealed record SystemEventPublishResult
{
    public required SystemEventPublishStatus Status { get; init; }

    public bool IsAccepted => Status == SystemEventPublishStatus.Accepted;
}
