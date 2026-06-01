namespace FluxFlow.Components.Timers.Options;

public sealed record TimerDelayOptions
{
    public const string ObjectTypeName = "object";

    public string InputType { get; init; } = ObjectTypeName;
    public string? Name { get; init; }
    public TimeSpan? Delay { get; init; }
    public double? DelayMilliseconds { get; init; }
    public int BoundedCapacity { get; init; } = 128;
}
