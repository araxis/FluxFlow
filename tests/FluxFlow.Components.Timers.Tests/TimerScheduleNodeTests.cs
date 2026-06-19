using FluxFlow.Components.Timers.Contracts;
using FluxFlow.Components.Timers.Nodes;
using FluxFlow.Components.Timers.Options;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Timers.Tests;

public sealed class TimerScheduleNodeTests
{
    [Fact]
    public async Task Schedule_EmitsConfiguredTickCount()
    {
        var startedAt = new DateTimeOffset(2026, 6, 2, 11, 59, 58, TimeSpan.Zero);
        var clock = new TrackingFakeTimeProvider(startedAt);
        await using var node = new TimerScheduleNode(
            new TimerScheduleSettings
            {
                Name = "cron",
                Cron = "* * * * * *",
                MaxTicks = 2,
                BoundedCapacity = 4
            },
            clock);
        var output = TimerTestSink.Link(node.Output);

        // The next occurrences are one second apart; advance the clock to release each.
        var scheduled = clock.TimerScheduled;
        await node.StartAsync();
        await scheduled.WaitAsync(TimeSpan.FromSeconds(30));
        var scheduled2 = clock.TimerScheduled;
        clock.Advance(TimeSpan.FromSeconds(1));
        await scheduled2.WaitAsync(TimeSpan.FromSeconds(30));
        clock.Advance(TimeSpan.FromSeconds(1));
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var ticks = TimerTestSink.Drain(output);
        ticks.Select(message => message.Payload.Sequence).ShouldBe([1, 2]);
        ticks.ShouldAllBe(message => message.Payload.Name == "cron");
        ticks.ShouldAllBe(message => message.Payload.Cron == "* * * * * *");
        ticks.ShouldAllBe(message => message.Payload.TimeZoneId == TimeZoneInfo.Utc.Id);
        ticks.ShouldAllBe(message => !message.CorrelationId.IsEmpty);
    }

    [Fact]
    public async Task Schedule_UsesConfiguredClockForDueTimes()
    {
        var startedAt = new DateTimeOffset(2026, 6, 2, 11, 59, 59, TimeSpan.Zero);
        var clock = new TrackingFakeTimeProvider(startedAt);
        await using var node = new TimerScheduleNode(
            new TimerScheduleSettings
            {
                Name = "noon",
                Cron = "0 0 12 * * *",
                MaxTicks = 1
            },
            clock);
        var output = TimerTestSink.Link(node.Output);

        var scheduled = clock.TimerScheduled;
        await node.StartAsync();
        await scheduled.WaitAsync(TimeSpan.FromSeconds(30));
        clock.Advance(TimeSpan.FromSeconds(1));
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var dueAt = new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero);
        var tick = TimerTestSink.Drain(output).ShouldHaveSingleItem();
        tick.Payload.StartedAt.ShouldBe(startedAt);
        tick.Payload.DueAt.ShouldBe(dueAt);
        tick.Payload.Timestamp.ShouldBe(dueAt);
    }

    [Fact]
    public async Task Schedule_EmitsLifecycleEvents()
    {
        var startedAt = new DateTimeOffset(2026, 6, 2, 11, 59, 59, TimeSpan.Zero);
        var clock = new TrackingFakeTimeProvider(startedAt);
        await using var node = new TimerScheduleNode(
            new TimerScheduleSettings
            {
                Name = "diag",
                Cron = "* * * * * *",
                MaxTicks = 1
            },
            clock);
        var output = TimerTestSink.Link(node.Output);
        var events = TimerTestSink.Link(node.Events);

        var scheduled = clock.TimerScheduled;
        await node.StartAsync();
        await scheduled.WaitAsync(TimeSpan.FromSeconds(30));
        clock.Advance(TimeSpan.FromSeconds(1));
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        TimerTestSink.Drain(output).ShouldHaveSingleItem();
        var eventNames = (await TimerTestSink.DrainUntilCompletedAsync(events))
            .Select(flowEvent => flowEvent.Name)
            .ToArray();
        eventNames.ShouldContain(TimerScheduleNode.Started);
        eventNames.ShouldContain(TimerScheduleNode.Tick);
        eventNames.ShouldContain(TimerScheduleNode.Stopped);
    }

    [Fact]
    public async Task Schedule_DisposeBeforeStartCompletesOutput()
    {
        await using var node = new TimerScheduleNode(
            new TimerScheduleSettings { Cron = "* * * * * *" });
        var output = TimerTestSink.Link(node.Output);

        await node.DisposeAsync();

        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        await output.Completion.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Schedule_AcceptsNamesAndQuestionMarks()
    {
        var node = new TimerScheduleNode(
            new TimerScheduleSettings
            {
                Cron = "0 0 12 ? JAN MON",
                MaxTicks = 1
            });

        node.ShouldNotBeNull();
    }

    [Fact]
    public void Schedule_AcceptsAConfiguredTimeZone()
    {
        var timeZone = TryFindTimeZone("America/New_York", "Eastern Standard Time");
        var node = new TimerScheduleNode(
            new TimerScheduleSettings
            {
                Cron = "0 0 12 * * *",
                TimeZone = timeZone,
                MaxTicks = 1
            });

        node.ShouldNotBeNull();
    }

    [Fact]
    public void Schedule_RejectsMissingCron()
        => Should.Throw<ArgumentException>(
            () => new TimerScheduleNode(new TimerScheduleSettings { Cron = "  " }))
            .Message.ShouldContain("Cron");

    [Fact]
    public void Schedule_RejectsInvalidCron()
        => Should.Throw<InvalidOperationException>(
            () => new TimerScheduleNode(new TimerScheduleSettings { Cron = "* * *" }))
            .Message.ShouldContain("five or six fields");

    [Fact]
    public void Schedule_RejectsInvalidMaxTicks()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new TimerScheduleNode(
                new TimerScheduleSettings
                {
                    Cron = "* * * * * *",
                    MaxTicks = 0
                }))
            .Message.ShouldContain("MaxTicks");

    [Fact]
    public void Schedule_RejectsInvalidBoundedCapacity()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new TimerScheduleNode(
                new TimerScheduleSettings
                {
                    Cron = "* * * * * *",
                    BoundedCapacity = 0
                }))
            .Message.ShouldContain("BoundedCapacity");

    private static TimeZoneInfo TryFindTimeZone(string ianaId, string windowsId)
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
