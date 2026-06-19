namespace FluxFlow.Components.Timers.Options;

/// <summary>
/// Configuration for <see cref="Nodes.TimerDelayNode{TInput}"/>. <see cref="Delay"/> is
/// required and cannot be negative; the node validates these in its constructor.
/// </summary>
public sealed record TimerDelaySettings
{
    public string Name { get; init; } = "delay";
    public required TimeSpan Delay { get; init; }
    public int BoundedCapacity { get; init; } = 128;
}
