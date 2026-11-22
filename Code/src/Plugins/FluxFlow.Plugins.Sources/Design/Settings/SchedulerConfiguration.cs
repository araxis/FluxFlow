namespace FluxFlow.Configuration.Components.Scheduling;

/// <summary>Serializable schedule settings; runtime dependencies belong to registration.</summary>
public sealed record SchedulerConfiguration : OutputComponentConfiguration
{
    public TimeSpan? Interval { get; init; }
    public string? Cron { get; init; }
    public DateTimeOffset? At { get; init; }
    public string? TimeZone { get; init; }
    public bool IncludeSeconds { get; init; }
    public int OutputCapacity { get; init; } = 128;

    public void Validate()
    {
        var count = (Interval.HasValue ? 1 : 0) + (Cron is not null ? 1 : 0) + (At.HasValue ? 1 : 0);
        if (count != 1) throw new ArgumentException("Specify exactly one of Interval, Cron or At.");
        if (Cron is null && (TimeZone is not null || IncludeSeconds))
            throw new ArgumentException("TimeZone and IncludeSeconds apply only to Cron.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(OutputCapacity);
        if (Interval is { } interval) ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
    }
}
