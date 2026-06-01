using FluxFlow.Components.Timers.Options;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Timers.Tests;

public sealed class CronScheduleTests
{
    [Fact]
    public void GetNextOccurrence_FiveFieldScheduleUsesMinuteHourDayMonthAndDayOfWeek()
    {
        var schedule = CronSchedule.Parse("0 12 ? * MON-FRI");
        var after = new DateTimeOffset(2026, 6, 1, 11, 59, 59, TimeSpan.Zero);

        var next = schedule.GetNextOccurrence(after, TimeZoneInfo.Utc);

        next.ShouldBe(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void GetNextOccurrence_SixFieldScheduleSupportsSeconds()
    {
        var schedule = CronSchedule.Parse("*/15 * * * * *");
        var after = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        var next = schedule.GetNextOccurrence(after, TimeZoneInfo.Utc);

        next.ShouldBe(new DateTimeOffset(2026, 6, 1, 0, 0, 15, TimeSpan.Zero));
    }

    [Fact]
    public void GetNextOccurrence_DayOfMonthAndDayOfWeekUseOrSemantics()
    {
        var schedule = CronSchedule.Parse("0 9 20 * MON");
        var after = new DateTimeOffset(2026, 6, 14, 0, 0, 0, TimeSpan.Zero);

        var next = schedule.GetNextOccurrence(after, TimeZoneInfo.Utc);

        next.ShouldBe(new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void GetNextOccurrence_NormalizesSevenAsSunday()
    {
        var schedule = CronSchedule.Parse("0 0 * * 7");
        var after = new DateTimeOffset(2026, 6, 6, 23, 59, 59, TimeSpan.Zero);

        var next = schedule.GetNextOccurrence(after, TimeZoneInfo.Utc);

        next.ShouldBe(new DateTimeOffset(2026, 6, 7, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Parse_RejectsUnsupportedFieldCount()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CronSchedule.Parse("* * *"));

        exception.Message.ShouldContain("five or six fields");
    }
}
