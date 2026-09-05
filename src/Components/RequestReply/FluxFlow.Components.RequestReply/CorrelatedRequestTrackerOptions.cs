namespace FluxFlow.Components.RequestReply;

public sealed record CorrelatedRequestTrackerOptions
{
    private TimeSpan _timeout = TimeSpan.FromSeconds(30);
    private TimeSpan _sweepInterval = TimeSpan.FromSeconds(1);
    private int _maxPending = 1024;

    /// <summary>How long a request can remain pending before it is failed.</summary>
    public TimeSpan Timeout
    {
        get => _timeout;
        init => _timeout = ValidatePositive(value, nameof(Timeout), "Timeout");
    }

    /// <summary>How often pending requests are checked for timeout.</summary>
    public TimeSpan SweepInterval
    {
        get => _sweepInterval;
        init => _sweepInterval = ValidatePositive(value, nameof(SweepInterval), "Sweep interval");
    }

    /// <summary>Maximum number of requests that may wait for a response.</summary>
    public int MaxPending
    {
        get => _maxPending;
        init => _maxPending = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(MaxPending),
                value,
                "Maximum pending count must be greater than zero.");
    }

    private static TimeSpan ValidatePositive(TimeSpan value, string name, string displayName)
        => value > TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(
                name,
                value,
                $"{displayName} must be greater than zero.");
}

public enum CorrelatedRequestStartResult
{
    Accepted = 0,
    DuplicateCorrelationId = 1,
    Stopped = 2,
    CapacityReached = 3
}
