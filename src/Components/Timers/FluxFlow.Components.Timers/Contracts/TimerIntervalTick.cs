namespace FluxFlow.Components.Timers.Contracts;

/// <summary>
/// Describes one emitted interval occurrence.
/// </summary>
public sealed record TimerIntervalTick(
    DateTimeOffset Timestamp,
    string Name,
    long Sequence,
    DateTimeOffset StartedAt,
    DateTimeOffset DueAt,
    TimeSpan Elapsed,
    TimeSpan Interval,
    TimeSpan Drift);
