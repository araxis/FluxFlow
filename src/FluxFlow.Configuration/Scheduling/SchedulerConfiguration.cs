using FluxFlow.Configuration.Links;

namespace FluxFlow.Configuration.Scheduling;

/// <summary>Serializable schedule settings; runtime dependencies belong to registration.</summary>
public sealed record SchedulerConfiguration
{
    public OneOrManyLinks Output { get; init; } = OneOrManyLinks.Empty;
    public OneOrManyLinks Events { get; init; } = OneOrManyLinks.Empty;

    public TimeSpan? Interval { get; init; }
    public string? Cron { get; init; }
    public DateTimeOffset? At { get; init; }
    public string? TimeZone { get; init; }
    public bool IncludeSeconds { get; init; }
    public int OutputCapacity { get; init; } = 128;
}
