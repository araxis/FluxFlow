namespace FluxFlow.Components.Timers.Options;

internal sealed record TimerThrottleSettings
{
    public required string Name { get; init; }
    public required string InputType { get; init; }
    public required TimeSpan Interval { get; init; }
    public required bool EmitFirstImmediately { get; init; }
    public required int BoundedCapacity { get; init; }
}
