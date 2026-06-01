namespace FluxFlow.Components.Timers.Options;

internal sealed record TimerDelaySettings
{
    public required string Name { get; init; }
    public required string InputType { get; init; }
    public required TimeSpan Delay { get; init; }
    public required int BoundedCapacity { get; init; }
}
