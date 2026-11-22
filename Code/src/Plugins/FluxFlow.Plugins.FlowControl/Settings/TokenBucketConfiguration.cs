namespace FluxFlow.Configuration.Components.RateLimiting;

public sealed record TokenBucketConfiguration
{
    public const string ResourceType = "rateLimiter.tokenBucket";
    public int TokenLimit { get; set; } = 10;
    public int TokensPerPeriod { get; set; } = 10;
    public TimeSpan ReplenishmentPeriod { get; set; } = TimeSpan.FromSeconds(1);
    public int QueueLimit { get; set; } = 128;

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(TokenLimit);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(TokensPerPeriod);
        ArgumentOutOfRangeException.ThrowIfNegative(QueueLimit);
        if (ReplenishmentPeriod < TimeSpan.FromMilliseconds(1) ||
            ReplenishmentPeriod > TimeSpan.FromMilliseconds(uint.MaxValue - 1))
            throw new ArgumentOutOfRangeException(nameof(ReplenishmentPeriod), "Refill interval must be between 1 millisecond and 49.17:02:47.294.");
    }
}
