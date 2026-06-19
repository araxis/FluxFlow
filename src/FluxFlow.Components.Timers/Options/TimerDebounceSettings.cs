namespace FluxFlow.Components.Timers.Options;

/// <summary>
/// Configuration for <see cref="Nodes.TimerDebounceNode{TInput}"/>. <see cref="QuietPeriod"/>
/// is required and must be greater than zero; the node validates these in its constructor.
/// </summary>
public sealed record TimerDebounceSettings
{
    public string Name { get; init; } = "debounce";
    public required TimeSpan QuietPeriod { get; init; }
    public int BoundedCapacity { get; init; } = 128;
}
