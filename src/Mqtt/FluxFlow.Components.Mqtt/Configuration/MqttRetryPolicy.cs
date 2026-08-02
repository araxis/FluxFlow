using System.Collections.Immutable;
using System.Text.Json.Serialization;
using FluxFlow.Resilience;

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
        => RetrySchedule.GetDelay(ToRetryPolicy(), attempt, jitterSample);

    internal RetryPolicy ToRetryPolicy()
        => new()
        {
            Strategy = Strategy switch
            {
                MqttRetryStrategy.Fixed => RetryBackoffStrategy.Fixed,
                MqttRetryStrategy.Linear => RetryBackoffStrategy.Linear,
                MqttRetryStrategy.Exponential => RetryBackoffStrategy.Exponential,
                _ => throw new ArgumentOutOfRangeException(nameof(Strategy))
            },
            InitialDelay = InitialDelay,
            Increment = Increment,
            MaximumDelay = MaximumDelay,
            MaximumAttempts = MaximumAttempts,
            MaximumDuration = MaximumDuration,
            JitterFactor = JitterFactor
        };
}

[JsonConverter(typeof(JsonStringEnumConverter<MqttRetryStrategy>))]
public enum MqttRetryStrategy
{
    Fixed = 0,
    Linear = 1,
    Exponential = 2
}
