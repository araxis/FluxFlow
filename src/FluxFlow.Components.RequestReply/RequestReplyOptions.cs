namespace FluxFlow.Components.RequestReply;

public sealed record RequestReplyOptions
{
    /// <summary>How long an in-flight request waits for its response before it is failed.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Bounded capacity of the intake/output/response queues (backpressure).</summary>
    public int Capacity { get; init; } = 128;

    /// <summary>How often the bridge sweeps for timed-out in-flight requests.</summary>
    public TimeSpan SweepInterval { get; init; } = TimeSpan.FromSeconds(1);
}

public static class RequestReplyErrorCodes
{
    public const int Unmatched = 8001;
    public const int ReplyFailed = 8002;
    public const int TimedOut = 8003;
    public const int DuplicateCorrelationId = 8004;
}

public static class RequestReplyEvents
{
    public const string Received = "requestreply.received";
    public const string Replied = "requestreply.replied";
    public const string TimedOut = "requestreply.timedout";
    public const string Unmatched = "requestreply.unmatched";
}
