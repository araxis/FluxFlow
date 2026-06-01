namespace FluxFlow.Components.Timers.Options;

internal sealed record TimerIntervalSettings
{
    public required string Name { get; init; }
    public required TimeSpan Interval { get; init; }
    public required TimeSpan InitialDelay { get; init; }
    public required bool EmitImmediately { get; init; }
    public required long? MaxTicks { get; init; }
    public required int BoundedCapacity { get; init; }
}
