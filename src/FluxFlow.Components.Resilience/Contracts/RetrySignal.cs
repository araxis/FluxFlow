namespace FluxFlow.Components.Resilience.Contracts;

public enum RetrySignalStatus
{
    Attempt = 0,
    Completed = 1,
    RetryScheduled = 2,
    Exhausted = 3,
    Cancelled = 4,
    Rejected = 5
}

public enum RetryFailureReason
{
    None = 0,
    Nak = 1,
    Timeout = 2,
    Cancelled = 3,
    Duplicate = 4,
    CapacityReached = 5,
    Stopped = 6
}

public sealed record RetrySignal<T>
{
    public required T Value { get; init; }

    public required RetrySignalStatus Status { get; init; }

    public required int Attempt { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public RetryFailureReason Reason { get; init; }

    public TimeSpan? NextDelay { get; init; }
}
