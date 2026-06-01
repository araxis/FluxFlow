namespace FluxFlow.Components.Timers.Options;

internal sealed record TimerScheduleSettings
{
    public required string Name { get; init; }
    public required string Cron { get; init; }
    public required CronSchedule Schedule { get; init; }
    public required TimeZoneInfo TimeZone { get; init; }
    public required long? MaxTicks { get; init; }
    public required int BoundedCapacity { get; init; }
}
