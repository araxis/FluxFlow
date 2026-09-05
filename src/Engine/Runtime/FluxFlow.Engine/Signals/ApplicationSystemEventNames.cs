namespace FluxFlow.Engine.Signals;

public static class ApplicationSystemEventNames
{
    public const string RuntimeCompleting = "flow.runtime.completing";
    public const string ComponentFaulted = "flow.component.faulted";
    public const string LinkConditionFailed = "flow.link.condition.failed";
    public const string LinkTargetRejected = "flow.link.target.rejected";
    public const string ResourceChanged = "flow.resource.changed";
    public const string RevisionChanged = "flow.revision.changed";
}

internal enum SystemEventPublishStatus
{
    Accepted = 1,
    Completed = 2
}

internal sealed record SystemEventPublishResult
{
    public required SystemEventPublishStatus Status { get; init; }

    public bool IsAccepted => Status == SystemEventPublishStatus.Accepted;
}
