using System.Collections.Immutable;

namespace FluxFlow.Components.Mqtt.Configuration;

public sealed record MqttRetryPolicy
{
    private IReadOnlyList<string> _retryCategories =
        ImmutableArray.Create("Availability", "Transient");

    public MqttRetryStrategy Strategy { get; init; } = MqttRetryStrategy.Exponential;

    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan Increment { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaximumDelay { get; init; } = TimeSpan.FromMinutes(1);

    public int? MaximumAttempts { get; init; }

    public TimeSpan? MaximumDuration { get; init; }

    public TimeSpan ResetAfter { get; init; } = TimeSpan.FromMinutes(5);

    public double JitterFactor { get; init; } = 0.2;

    public IReadOnlyList<string> RetryCategories
    {
        get => _retryCategories;
        init => _retryCategories = value is null || value.Count == 0
            ? ImmutableArray<string>.Empty
            : value.Select(static category => category?.Trim())
                .Where(static category => !string.IsNullOrWhiteSpace(category))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray();
    }

    public TimeSpan GetDelay(int attempt, double jitterSample = 0.5)
    {
        if (attempt <= 0)
            throw new ArgumentOutOfRangeException(nameof(attempt));
        if (jitterSample is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(jitterSample));

        var baseDelay = Strategy switch
        {
            MqttRetryStrategy.Fixed => InitialDelay,
            MqttRetryStrategy.Linear => InitialDelay + TimeSpan.FromTicks(
                checked(Increment.Ticks * (long)(attempt - 1))),
            MqttRetryStrategy.Exponential => TimeSpan.FromTicks(
                ScaleExponential(InitialDelay.Ticks, attempt - 1)),
            _ => throw new ArgumentOutOfRangeException(nameof(Strategy))
        };

        if (baseDelay > MaximumDelay)
            baseDelay = MaximumDelay;

        var jitter = 1 + ((jitterSample * 2 - 1) * JitterFactor);
        return TimeSpan.FromTicks(Math.Max(0, (long)(baseDelay.Ticks * jitter)));
    }

    private static long ScaleExponential(long ticks, int exponent)
    {
        if (ticks <= 0)
            return 0;

        for (var index = 0; index < exponent; index++)
        {
            if (ticks > long.MaxValue / 2)
                return long.MaxValue;
            ticks *= 2;
        }

        return ticks;
    }
}

public enum MqttRetryStrategy
{
    Fixed = 0,
    Linear = 1,
    Exponential = 2
}
