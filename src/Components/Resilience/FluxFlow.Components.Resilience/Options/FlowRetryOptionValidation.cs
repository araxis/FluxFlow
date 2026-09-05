using FluxFlow.Resilience;

namespace FluxFlow.Components.Resilience.Options;

internal static class FlowRetryOptionValidation
{
    public static FlowRetryOptions Validate(FlowRetryOptions? options)
    {
        var resolved = options ?? new FlowRetryOptions();
        if (!Enum.IsDefined(resolved.Strategy))
            throw new ArgumentOutOfRangeException(nameof(options), resolved.Strategy, "Retry strategy is not supported.");
        if (resolved.InitialDelayMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Initial delay must not be negative.");
        if (resolved.IncrementMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Delay increment must not be negative.");
        if (resolved.MaximumDelayMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum delay must not be negative.");
        if (resolved.MaximumAttempts is <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum attempts must be greater than zero when set.");
        if (resolved.MaximumDurationMilliseconds is <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum duration must be greater than zero when set.");
        if (resolved.JitterFactor is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(options), "Jitter factor must be between zero and one.");
        if (resolved.AttemptTimeoutMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Attempt timeout must be greater than zero.");
        if (resolved.Capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Capacity must be greater than zero.");

        return resolved with
        {
            Name = string.IsNullOrWhiteSpace(resolved.Name)
                ? "retry"
                : resolved.Name.Trim()
        };
    }

    public static RetryPolicy CreatePolicy(FlowRetryOptions options)
        => new()
        {
            Strategy = options.Strategy,
            InitialDelay = TimeSpan.FromMilliseconds(options.InitialDelayMilliseconds),
            Increment = TimeSpan.FromMilliseconds(options.IncrementMilliseconds),
            MaximumDelay = TimeSpan.FromMilliseconds(options.MaximumDelayMilliseconds),
            MaximumAttempts = options.MaximumAttempts,
            MaximumDuration = options.MaximumDurationMilliseconds is { } milliseconds
                ? TimeSpan.FromMilliseconds(milliseconds)
                : null,
            JitterFactor = options.JitterFactor
        };
}
