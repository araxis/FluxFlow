namespace FluxFlow.Components.Timers.Options;

/// <summary>
/// Configuration for <see cref="Nodes.TimerIntervalNode"/>. <see cref="Interval"/> is
/// required and must be greater than zero; the node validates these in its constructor.
/// </summary>
public sealed record TimerIntervalSettings
{
    public string Name { get; init; } = "interval";
    public required TimeSpan Interval { get; init; }
    public TimeSpan InitialDelay { get; init; } = TimeSpan.Zero;
    public bool EmitImmediately { get; init; }
    public long? MaxTicks { get; init; }
    public int BoundedCapacity { get; init; } = 128;
}
