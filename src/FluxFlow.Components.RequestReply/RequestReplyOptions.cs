namespace FluxFlow.Components.RequestReply;

public sealed record RequestReplyOptions
{
    private RequestReplyMode _mode = RequestReplyMode.RequestReply;
    private TimeSpan _timeout = TimeSpan.FromSeconds(30);
    private int _capacity = 128;
    private TimeSpan _sweepInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Whether the trigger waits for a correlated response (<see cref="RequestReplyMode.RequestReply"/>)
    /// or publishes the request and acknowledges the caller immediately
    /// (<see cref="RequestReplyMode.FireAndForget"/>).
    /// </summary>
    public RequestReplyMode Mode
    {
        get => _mode;
        init => _mode = ValidateMode(value);
    }

    /// <summary>How long an in-flight request waits for its response before it is failed.</summary>
    public TimeSpan Timeout
    {
        get => _timeout;
        init => _timeout = ValidatePositive(value, nameof(Timeout), "Timeout");
    }

    /// <summary>Bounded capacity of the intake/output/response queues (backpressure).</summary>
    public int Capacity
    {
        get => _capacity;
        init => _capacity = ValidateCapacity(value);
    }

    /// <summary>How often the bridge sweeps for timed-out in-flight requests.</summary>
    public TimeSpan SweepInterval
    {
        get => _sweepInterval;
        init => _sweepInterval = ValidatePositive(value, nameof(SweepInterval), "Sweep interval");
    }

    private static RequestReplyMode ValidateMode(RequestReplyMode value)
        => Enum.IsDefined(value)
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Request/reply mode is not supported.");

    private static int ValidateCapacity(int value)
        => value > 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Capacity must be greater than zero.");

    private static TimeSpan ValidatePositive(TimeSpan value, string name, string displayName)
        => value > TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(
                name,
                value,
                $"{displayName} must be greater than zero.");
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
