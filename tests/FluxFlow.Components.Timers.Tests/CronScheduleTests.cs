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

    [Fact]
    public void GetNextOccurrence_ValueWithStepRunsFromValueToFieldMaximum()
    {
        var schedule = CronSchedule.Parse("5/20 * * * *");
        var after = new DateTimeOffset(2026, 6, 1, 0, 6, 0, TimeSpan.Zero);

        var next = schedule.GetNextOccurrence(after, TimeZoneInfo.Utc);

        next.ShouldBe(new DateTimeOffset(2026, 6, 1, 0, 25, 0, TimeSpan.Zero));
    }

    [Fact]
    public void GetNextOccurrence_SpringForwardGapFiresAtFirstValidInstant_NewYork()
    {
        // America/New_York skips 02:00-02:59 local on 2026-03-08.
        var timeZone = FindTimeZone("America/New_York", "Eastern Standard Time");
        var schedule = CronSchedule.Parse("30 2 * * *");
        var after = new DateTimeOffset(2026, 3, 8, 0, 0, 0, TimeSpan.FromHours(-5));

        var next = schedule.GetNextOccurrence(after, timeZone);

        next.ShouldBe(new DateTimeOffset(2026, 3, 8, 3, 0, 0, TimeSpan.FromHours(-4)));
    }

    [Fact]
    public void GetNextOccurrence_SpringForwardGapFiresAtFirstValidInstant_Berlin()
    {
        // Europe/Berlin skips 02:00-02:59 local on 2026-03-29.
        var timeZone = FindTimeZone("Europe/Berlin", "W. Europe Standard Time");
        var schedule = CronSchedule.Parse("30 2 * * *");
        var after = new DateTimeOffset(2026, 3, 29, 0, 0, 0, TimeSpan.FromHours(1));

        var next = schedule.GetNextOccurrence(after, timeZone);

        next.ShouldBe(new DateTimeOffset(2026, 3, 29, 3, 0, 0, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void GetNextOccurrence_MinutelyScheduleDoesNotStallAcrossFallBack()
    {
        // America/New_York repeats 01:00-01:59 local on 2026-11-01.
        var timeZone = FindTimeZone("America/New_York", "Eastern Standard Time");
        var schedule = CronSchedule.Parse("* * * * *");
        var current = new DateTimeOffset(2026, 11, 1, 1, 58, 0, TimeSpan.FromHours(-4));

        for (var occurrence = 0; occurrence < 6; occurrence++)
        {
            var next = schedule.GetNextOccurrence(current, timeZone);

            next.ShouldNotBeNull();
            next.Value.UtcDateTime.ShouldBeGreaterThan(current.UtcDateTime);
            current = next.Value;
        }

        current.UtcDateTime.ShouldBeGreaterThan(
            new DateTimeOffset(2026, 11, 1, 2, 0, 0, TimeSpan.FromHours(-5)).UtcDateTime);
    }

    private static TimeZoneInfo FindTimeZone(string ianaId, string windowsId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(ianaId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
        }
    }
}
