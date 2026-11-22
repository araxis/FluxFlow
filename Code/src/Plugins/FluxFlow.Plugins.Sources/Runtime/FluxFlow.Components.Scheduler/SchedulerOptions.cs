namespace FluxFlow.Components.Scheduler;

public sealed class SchedulerOptions
{
    /// <summary>Returns the next instant strictly after the argument, or null to finish.</summary>
    public required Func<DateTimeOffset, DateTimeOffset?> NextOccurrence { get; init; }
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
    public int OutputCapacity { get; init; } = 128;
}
