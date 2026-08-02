namespace FluxFlow.Components.Timers.Contracts;

/// <summary>
/// Describes one emitted schedule occurrence.
/// </summary>
public sealed record TimerScheduleTick(
    DateTimeOffset Timestamp,
    string Name,
    long Sequence,
    DateTimeOffset StartedAt,
    DateTimeOffset DueAt,
    string Cron,
    string TimeZoneId,
    TimeSpan Drift);
