namespace FluxFlow.Components.Timers.Options;

/// <summary>
/// Configuration for <see cref="Nodes.TimerScheduleNode"/>. Supply a five- or six-field
/// <see cref="Cron"/> expression (six fields when seconds are needed) and an optional
/// <see cref="TimeZone"/> (defaults to UTC). The node compiles and validates the cron in
/// its constructor.
/// </summary>
public sealed record TimerScheduleSettings
{
    public string Name { get; init; } = "schedule";
    public required string Cron { get; init; }
    public TimeZoneInfo TimeZone { get; init; } = TimeZoneInfo.Utc;
    public long? MaxTicks { get; init; }
    public int BoundedCapacity { get; init; } = 128;
}
