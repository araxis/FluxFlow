using System.ComponentModel.DataAnnotations;
using FluxFlow.Core.Messaging;
using Polly.Retry;
using FluxFlow.Configuration.Application.Links;
using FluxFlow.Configuration.Expressions;
using Polly;

namespace FluxFlow.Configuration.Components.Resilience;

/// <summary>Retry settings and request/reply links; transient errors are retried by default.</summary>
public sealed record RetryConfiguration : ComponentConfiguration
{
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxRetryAttempts { get; set; } = 3;
    public TimeSpan Delay { get; set; } = TimeSpan.FromSeconds(2);
    public DelayBackoffType BackoffType { get; set; } = DelayBackoffType.Constant;
    public bool UseJitter { get; set; }
    public TimeSpan? MaxDelay { get; set; }
    public string? Engine { get; set; }
    public ExpressionSource? ShouldRetry { get; set; }
    public int OutputCapacity { get; set; } = 128;
    public OneOrManyPortReferences Request { get; init; } = OneOrManyPortReferences.Empty;
    public OneOrManyPortReferences Reply { get; init; } = OneOrManyPortReferences.Empty;

    public RetryStrategyOptions<FlowMessage> CreatePolicy()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(OutputCapacity);
        if (AttemptTimeout != Timeout.InfiniteTimeSpan &&
            (AttemptTimeout <= TimeSpan.Zero || AttemptTimeout.TotalMilliseconds > uint.MaxValue - 1))
            throw new ArgumentOutOfRangeException(nameof(AttemptTimeout));
        if (ShouldRetry is not null) ArgumentException.ThrowIfNullOrWhiteSpace(Engine);
        else if (Engine is not null) throw new ArgumentException("Engine requires ShouldRetry.");
        var policy = new RetryStrategyOptions<FlowMessage>
        {
            MaxRetryAttempts = MaxRetryAttempts,
            Delay = Delay,
            BackoffType = BackoffType,
            UseJitter = UseJitter,
            MaxDelay = MaxDelay,
            ShouldHandle = args => ValueTask.FromResult(args.Outcome.Result?.Error?.IsTransient == true)
        };
        Validator.ValidateObject(policy, new ValidationContext(policy), validateAllProperties: true);
        return policy;
    }
}
