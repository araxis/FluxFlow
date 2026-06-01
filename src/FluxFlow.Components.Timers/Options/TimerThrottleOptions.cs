namespace FluxFlow.Components.Timers.Options;

public sealed record TimerThrottleOptions
{
    public const string ObjectTypeName = "object";

    public string InputType { get; init; } = ObjectTypeName;
    public string? Name { get; init; }
    public TimeSpan? Interval { get; init; }
    public double? IntervalMilliseconds { get; init; }
    public bool EmitFirstImmediately { get; init; } = true;
    public int BoundedCapacity { get; init; } = 128;
}
