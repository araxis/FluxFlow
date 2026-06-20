namespace FluxFlow.Components.RequestReply;

public sealed record CorrelatedRequestTrackerOptions
{
    /// <summary>How long a request can remain pending before it is failed.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How often pending requests are checked for timeout.</summary>
    public TimeSpan SweepInterval { get; init; } = TimeSpan.FromSeconds(1);
}
public enum CorrelatedRequestStartResult
{
    Accepted = 0,
    DuplicateCorrelationId = 1,
    Stopped = 2
}
