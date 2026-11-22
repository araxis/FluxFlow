using Cronos;

namespace FluxFlow.Components.Scheduler;

/// <summary>Next-occurrence functions for SchedulerOptions.</summary>
public static class Schedules
{
    /// <summary>First tick is one interval after startup. Missed ticks are skipped.</summary>
    public static Func<DateTimeOffset, DateTimeOffset?> Every(TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        return after => after + interval;
    }

    public static Func<DateTimeOffset, DateTimeOffset?> At(DateTimeOffset instant)
        => after => instant > after ? instant : null;

    /// <summary>Uses UTC unless a time zone is supplied. Five fields, or six with seconds.</summary>
    public static Func<DateTimeOffset, DateTimeOffset?> Cron(string expression,
        TimeZoneInfo? timeZone = null, bool includeSeconds = false)
    {
        var cron = CronExpression.Parse(expression,
            includeSeconds ? CronFormat.IncludeSeconds : CronFormat.Standard);
        var zone = timeZone ?? TimeZoneInfo.Utc;
        return after => cron.GetNextOccurrence(after, zone);
    }
}
