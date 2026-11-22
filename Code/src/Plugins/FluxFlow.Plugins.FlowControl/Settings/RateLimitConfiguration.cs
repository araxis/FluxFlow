using System.Diagnostics.CodeAnalysis;
using FluxFlow.Configuration.Application.Links;

namespace FluxFlow.Configuration.Components.RateLimiting;

/// <summary>References a native RateLimiter resource owned by the host provider.</summary>
public sealed record RateLimitConfiguration : ComponentConfiguration
{
    public string? Limiter { get; set; }
    public TimeSpan ResponseTimeout { get; set; } = Timeout.InfiniteTimeSpan;
    public int OutputCapacity { get; set; } = 128;
    public OneOrManyPortReferences Request { get; init; } = OneOrManyPortReferences.Empty;
    public OneOrManyPortReferences Reply { get; init; } = OneOrManyPortReferences.Empty;

    [MemberNotNull(nameof(Limiter))]
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Limiter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(OutputCapacity);
        if (ResponseTimeout != Timeout.InfiniteTimeSpan &&
            (ResponseTimeout <= TimeSpan.Zero || ResponseTimeout.TotalMilliseconds > uint.MaxValue - 1))
            throw new ArgumentOutOfRangeException(nameof(ResponseTimeout));
    }
}
