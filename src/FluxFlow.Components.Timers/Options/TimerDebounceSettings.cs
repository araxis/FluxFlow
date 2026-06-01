namespace FluxFlow.Components.Timers.Options;

internal sealed record TimerDebounceSettings
{
    public required string Name { get; init; }
    public required string InputType { get; init; }
    public required TimeSpan QuietPeriod { get; init; }
    public required int BoundedCapacity { get; init; }
}
