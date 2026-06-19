namespace FluxFlow.Components.Timers.Options;

/// <summary>
/// Configuration for <see cref="Nodes.TimerThrottleNode{TInput}"/>. <see cref="Interval"/>
/// is required and must be greater than zero; the node validates these in its constructor.
/// </summary>
public sealed record TimerThrottleSettings
{
    public string Name { get; init; } = "throttle";
    public required TimeSpan Interval { get; init; }
    public bool EmitFirstImmediately { get; init; } = true;
    public int BoundedCapacity { get; init; } = 128;
}
