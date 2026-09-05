using FluxFlow.Resilience;

namespace FluxFlow.Components.Resilience.Options;

public sealed record FlowRetryOptions
{
    public string? Name { get; init; }

    public RetryBackoffStrategy Strategy { get; init; } = RetryBackoffStrategy.Exponential;

    public int InitialDelayMilliseconds { get; init; } = 1_000;

    public int IncrementMilliseconds { get; init; } = 1_000;

    public int MaximumDelayMilliseconds { get; init; } = 60_000;

    public int? MaximumAttempts { get; init; } = 3;

    public int? MaximumDurationMilliseconds { get; init; }

    public double JitterFactor { get; init; }

    public int AttemptTimeoutMilliseconds { get; init; } = 30_000;

    /// <summary>
    /// Capacity shared by queued inputs, logical retry operations, pending feedback,
    /// and reliable normal-data result output.
    /// </summary>
    public int Capacity { get; init; } = 128;
}
