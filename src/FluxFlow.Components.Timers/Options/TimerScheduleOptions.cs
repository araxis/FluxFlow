namespace FluxFlow.Components.Timers.Options;

public sealed record TimerScheduleOptions
{
    public string? Name { get; init; }
    public string? Cron { get; init; }
    public string? Expression { get; init; }
    public string? TimeZoneId { get; init; }
    public long? MaxTicks { get; init; }
    public int BoundedCapacity { get; init; } = 128;
}
