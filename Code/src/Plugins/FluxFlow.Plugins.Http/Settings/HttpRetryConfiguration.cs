using Polly;

namespace FluxFlow.Configuration.Components.Http;

public sealed class HttpRetryConfiguration
{
    public int MaxRetryAttempts { get; init; } = 3;
    public TimeSpan Delay { get; init; } = TimeSpan.FromSeconds(2);
    public DelayBackoffType BackoffType { get; init; } = DelayBackoffType.Exponential;
    public bool UseJitter { get; init; } = true;
    public TimeSpan? MaxDelay { get; init; } = TimeSpan.FromSeconds(30);
    public bool ShouldRetryAfterHeader { get; init; } = true;
    /// <summary>Enables retries for POST, PATCH, PUT, DELETE and CONNECT as well as safe methods.</summary>
    public bool RetryUnsafeMethods { get; init; }
}
