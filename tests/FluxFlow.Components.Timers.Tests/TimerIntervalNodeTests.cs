using FluxFlow.Components.Timers.Contracts;
using FluxFlow.Components.Timers.Nodes;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Nodes;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Timers.Tests;

public sealed class TimerIntervalNodeTests
{
    [Fact]
    public async Task Interval_EmitsConfiguredTickCount()
    {
        var startedAt = new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero);
        var clock = new TrackingFakeTimeProvider(startedAt);
        await using var node = new TimerIntervalNode(
            new TimerIntervalSettings
            {
                Name = "poll",
                Interval = TimeSpan.FromMilliseconds(10),
                EmitImmediately = true,
                MaxTicks = 3,
                BoundedCapacity = 8
            },
            clock);
        var output = TimerTestSink.Link(node.Output);

        // First tick fires immediately; advance the clock twice to release the next two.
        var scheduled = clock.TimerScheduled;
        await node.StartAsync();
        await scheduled.WaitAsync(TimeSpan.FromSeconds(30));
        var scheduled2 = clock.TimerScheduled;
        clock.Advance(TimeSpan.FromMilliseconds(10));
        await scheduled2.WaitAsync(TimeSpan.FromSeconds(30));
        clock.Advance(TimeSpan.FromMilliseconds(10));
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var ticks = TimerTestSink.Drain(output);
        ticks.Select(message => message.Payload.Sequence).ShouldBe([1, 2, 3]);
        ticks.ShouldAllBe(message => message.Payload.Name == "poll");
        ticks.ShouldAllBe(message => message.Payload.Interval == TimeSpan.FromMilliseconds(10));
    }

    [Fact]
    public async Task Interval_HonorsInitialDelay()
    {
        var startedAt = new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero);
        var clock = new TrackingFakeTimeProvider(startedAt);
        await using var node = new TimerIntervalNode(
            new TimerIntervalSettings
            {
                Interval = TimeSpan.FromMilliseconds(100),
                InitialDelay = TimeSpan.FromMilliseconds(40),
                MaxTicks = 1
            },
            clock);
        var output = TimerTestSink.Link(node.Output);

        var scheduled = clock.TimerScheduled;
        await node.StartAsync();
        await scheduled.WaitAsync(TimeSpan.FromSeconds(30));
        // Nothing should be emitted before the 40ms initial delay elapses.
        output.TryReceive(out _).ShouldBeFalse();
        clock.Advance(TimeSpan.FromMilliseconds(40));
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var tick = TimerTestSink.Drain(output).ShouldHaveSingleItem();
        tick.Payload.Sequence.ShouldBe(1);
        tick.Payload.DueAt.ShouldBe(startedAt.AddMilliseconds(40));
        tick.Payload.Timestamp.ShouldBe(startedAt.AddMilliseconds(40));
    }

    [Fact]
    public async Task Interval_MintsAFreshCorrelationIdPerTick()
    {
        var clock = new TrackingFakeTimeProvider(new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        await using var node = new TimerIntervalNode(
            new TimerIntervalSettings
            {
                Interval = TimeSpan.FromMilliseconds(10),
                EmitImmediately = true,
                MaxTicks = 2
            },
            clock);
        var output = TimerTestSink.Link(node.Output);

        var scheduled = clock.TimerScheduled;
        await node.StartAsync();
        await scheduled.WaitAsync(TimeSpan.FromSeconds(30));
        clock.Advance(TimeSpan.FromMilliseconds(10));
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var ticks = TimerTestSink.Drain(output);
        ticks.Count.ShouldBe(2);
        ticks[0].CorrelationId.ShouldNotBe(ticks[1].CorrelationId);
        ticks.ShouldAllBe(message => !message.CorrelationId.IsEmpty);
    }

    [Fact]
    public async Task Interval_CompleteStopsTimer()
    {
        var clock = new TrackingFakeTimeProvider(new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        await using var node = new TimerIntervalNode(
            new TimerIntervalSettings
            {
                Interval = TimeSpan.FromMilliseconds(10),
                EmitImmediately = true,
                BoundedCapacity = 8
            },
            clock);
        var output = TimerTestSink.Link(node.Output);

        await node.StartAsync();
        await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Interval_DisposeBeforeStartCompletesOutput()
    {
        await using var node = new TimerIntervalNode(
            new TimerIntervalSettings { Interval = TimeSpan.FromMilliseconds(10) });
        var output = TimerTestSink.Link(node.Output);

        await node.DisposeAsync();

        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        await output.Completion.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Interval_EmitsLifecycleEvents()
    {
        var clock = new TrackingFakeTimeProvider(new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        await using var node = new TimerIntervalNode(
            new TimerIntervalSettings
            {
                Name = "diag",
                Interval = TimeSpan.FromMilliseconds(10),
                EmitImmediately = true,
                MaxTicks = 1
            },
            clock);
        var output = TimerTestSink.Link(node.Output);
        var events = TimerTestSink.Link(node.Events);

        await node.StartAsync();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        TimerTestSink.Drain(output).ShouldHaveSingleItem();
        var eventNames = (await TimerTestSink.DrainUntilCompletedAsync(events))
            .Select(flowEvent => flowEvent.Name)
            .ToArray();
        eventNames.ShouldContain(TimerIntervalNode.Started);
        eventNames.ShouldContain(TimerIntervalNode.Tick);
        eventNames.ShouldContain(TimerIntervalNode.Stopped);
    }

    [Fact]
    public void Interval_RejectsInvalidInterval()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new TimerIntervalNode(
                new TimerIntervalSettings { Interval = TimeSpan.Zero }))
            .Message.ShouldContain("Interval");

    [Fact]
    public void Interval_RejectsNegativeInitialDelay()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new TimerIntervalNode(
                new TimerIntervalSettings
                {
                    Interval = TimeSpan.FromMilliseconds(10),
                    InitialDelay = TimeSpan.FromMilliseconds(-1)
                }))
            .Message.ShouldContain("InitialDelay");

    [Fact]
    public void Interval_RejectsInvalidMaxTicks()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new TimerIntervalNode(
                new TimerIntervalSettings
                {
                    Interval = TimeSpan.FromMilliseconds(10),
                    MaxTicks = 0
                }))
            .Message.ShouldContain("MaxTicks");

    [Fact]
    public void Interval_RejectsInvalidBoundedCapacity()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new TimerIntervalNode(
                new TimerIntervalSettings
                {
                    Interval = TimeSpan.FromMilliseconds(10),
                    BoundedCapacity = 0
                }))
            .Message.ShouldContain("BoundedCapacity");

    [Fact]
    public void Interval_RejectsNullSettings()
        => Should.Throw<ArgumentNullException>(() => new TimerIntervalNode(null!));
}
