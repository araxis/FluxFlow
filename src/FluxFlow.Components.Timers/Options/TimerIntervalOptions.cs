namespace FluxFlow.Components.Timers.Options;

public sealed record TimerIntervalOptions
{
    public string? Name { get; init; }
    public TimeSpan? Interval { get; init; }
    public double? IntervalMilliseconds { get; init; }
    public TimeSpan? InitialDelay { get; init; }
    public double? InitialDelayMilliseconds { get; init; }
    public bool EmitImmediately { get; init; }
    public long? MaxTicks { get; init; }
    public int BoundedCapacity { get; init; } = 128;
}
