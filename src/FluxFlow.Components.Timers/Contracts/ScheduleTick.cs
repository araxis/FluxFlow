namespace FluxFlow.Components.Timers.Contracts;

public sealed record ScheduleTick
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string Name { get; init; }
    public required long Sequence { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset DueAt { get; init; }
    public required string Cron { get; init; }
    public required string TimeZoneId { get; init; }
    public required TimeSpan Drift { get; init; }
}
