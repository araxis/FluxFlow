namespace FluxFlow.Configuration.Components.Batching;

public sealed record BatchConfiguration : ComponentConfiguration
{
    public int BatchSize { get; init; }
    public TimeSpan MaxWait { get; init; }
    public int OutputCapacity { get; init; } = 128;

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(BatchSize);
        if (MaxWait <= TimeSpan.Zero || MaxWait.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(MaxWait));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(OutputCapacity);
    }
}
