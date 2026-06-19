namespace FluxFlow.Components.RequestReply;

public sealed record RequestReplyOptions
{
    /// <summary>
    /// Whether the trigger waits for a correlated response (<see cref="RequestReplyMode.RequestReply"/>)
    /// or publishes the request and acknowledges the caller immediately
    /// (<see cref="RequestReplyMode.FireAndForget"/>).
    /// </summary>
    public RequestReplyMode Mode { get; init; } = RequestReplyMode.RequestReply;

    /// <summary>How long an in-flight request waits for its response before it is failed.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Bounded capacity of the intake/output/response queues (backpressure).</summary>
    public int Capacity { get; init; } = 128;

    /// <summary>How often the bridge sweeps for timed-out in-flight requests.</summary>
    public TimeSpan SweepInterval { get; init; } = TimeSpan.FromSeconds(1);
}

/// <summary>How a trigger handles an inbound request once it has been correlated and published.</summary>
public enum RequestReplyMode
{
    /// <summary>Publish the request and hold it in-flight until the correlated response returns (or it times out).</summary>
    RequestReply,

    /// <summary>Publish the request and acknowledge the caller immediately — no response is awaited.</summary>
    FireAndForget
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
    public const string Published = "requestreply.published";
    public const string Replied = "requestreply.replied";
    public const string TimedOut = "requestreply.timedout";
    public const string Unmatched = "requestreply.unmatched";
}
