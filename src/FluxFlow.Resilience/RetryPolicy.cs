using System.Text.Json.Serialization;

namespace FluxFlow.Resilience;

public sealed record RetryPolicy
{
    private RetryBackoffStrategy _strategy = RetryBackoffStrategy.Exponential;
    private TimeSpan _initialDelay = TimeSpan.FromSeconds(1);
    private TimeSpan _increment = TimeSpan.FromSeconds(1);
    private TimeSpan _maximumDelay = TimeSpan.FromMinutes(1);
    private int? _maximumAttempts;
    private TimeSpan? _maximumDuration;
    private double _jitterFactor;

    public RetryBackoffStrategy Strategy
    {
        get => _strategy;
        init => _strategy = Enum.IsDefined(value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(Strategy), value, "Retry strategy is not supported.");
    }

    public TimeSpan InitialDelay
    {
        get => _initialDelay;
        init => _initialDelay = ValidateNonNegative(value, nameof(InitialDelay));
    }

    public TimeSpan Increment
    {
        get => _increment;
        init => _increment = ValidateNonNegative(value, nameof(Increment));
    }

    public TimeSpan MaximumDelay
    {
        get => _maximumDelay;
        init => _maximumDelay = ValidateNonNegative(value, nameof(MaximumDelay));
    }

    public int? MaximumAttempts
    {
        get => _maximumAttempts;
        init => _maximumAttempts = value is null or > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(MaximumAttempts), value, "Maximum attempts must be greater than zero.");
    }

    public TimeSpan? MaximumDuration
    {
        get => _maximumDuration;
        init => _maximumDuration = value is null || value > TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(nameof(MaximumDuration), value, "Maximum duration must be greater than zero.");
    }

    public double JitterFactor
    {
        get => _jitterFactor;
        init => _jitterFactor = value is >= 0 and <= 1
            ? value
            : throw new ArgumentOutOfRangeException(nameof(JitterFactor), value, "Jitter factor must be between zero and one.");
    }

    private static TimeSpan ValidateNonNegative(TimeSpan value, string name)
        => value >= TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(name, value, "Retry delays must not be negative.");
}

[JsonConverter(typeof(JsonStringEnumConverter<RetryBackoffStrategy>))]
public enum RetryBackoffStrategy
{
    Fixed = 0,
    Linear = 1,
    Exponential = 2
}
