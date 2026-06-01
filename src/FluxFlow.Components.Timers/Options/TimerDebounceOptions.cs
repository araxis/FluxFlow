namespace FluxFlow.Components.Timers.Options;

public sealed record TimerDebounceOptions
{
    public const string ObjectTypeName = "object";

    public string InputType { get; init; } = ObjectTypeName;
    public string? Name { get; init; }
    public TimeSpan? QuietPeriod { get; init; }
    public double? QuietPeriodMilliseconds { get; init; }
    public int BoundedCapacity { get; init; } = 128;
}
