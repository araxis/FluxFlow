using FluxFlow.Components.Timers.Contracts;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Timers.Tests;

public sealed class TimerClockTests
{
    [Fact]
    public async Task Interval_UsesConfiguredClock()
    {
        var startedAt = new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero);
        var clock = new TrackingFakeTimeProvider(startedAt);
        var runtimeNode = CreateNode(
            TimerComponentTypes.Interval,
            new
            {
                name = "clocked",
                intervalMilliseconds = 100,
                initialDelayMilliseconds = 50,
                maxTicks = 2
            },
            clock);
        var output = LinkOutput<TimerTick>(runtimeNode);

        // Capture the initial-delay timer's registration before starting the loop.
        var scheduled = clock.TimerScheduled;
        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(30));

        // The first tick is due after the 50ms initial delay; advancing releases the
        // pending Task.Delay so the loop emits and registers the next interval delay.
        var (first, scheduled2) = await AdvanceUntilReceivedAsync(
            clock, output, scheduled, TimeSpan.FromMilliseconds(50));
        var (second, _) = await AdvanceUntilReceivedAsync(
            clock, output, scheduled2, TimeSpan.FromMilliseconds(100));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var ticks = new[] { first, second };
        ticks.Select(tick => tick.Sequence).ShouldBe([1, 2]);
        ticks.Select(tick => tick.StartedAt).Distinct().ShouldBe([startedAt]);
        ticks.Select(tick => tick.DueAt).ShouldBe(
        [
            startedAt.AddMilliseconds(50),
            startedAt.AddMilliseconds(150)
        ]);
        ticks.Select(tick => tick.Timestamp).ShouldBe(ticks.Select(tick => tick.DueAt));
    }

    [Fact]
    public async Task Schedule_UsesConfiguredClock()
    {
        var startedAt = new DateTimeOffset(2026, 6, 2, 11, 59, 59, TimeSpan.Zero);
        var clock = new TrackingFakeTimeProvider(startedAt);
        var runtimeNode = CreateNode(
            TimerComponentTypes.Schedule,
            new
            {
                name = "noon",
                cron = "0 0 12 * * *",
                maxTicks = 1
            },
            clock);
        var output = LinkOutput<ScheduleTick>(runtimeNode);

        // Capture the next-occurrence timer's registration before starting the loop.
        var scheduled = clock.TimerScheduled;
        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(30));

        // The next noon occurrence is one second away; releasing that delay emits the tick.
        var (tick, _) = await AdvanceUntilReceivedAsync(
            clock, output, scheduled, TimeSpan.FromSeconds(1));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var dueAt = new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero);
        tick.StartedAt.ShouldBe(startedAt);
        tick.DueAt.ShouldBe(dueAt);
        tick.Timestamp.ShouldBe(dueAt);
    }

    [Fact]
    public async Task Delay_UsesConfiguredClock()
    {
        var clock = new TrackingFakeTimeProvider(new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        var runtimeNode = CreateNode(
            TimerComponentTypes.Delay,
            new
            {
                inputType = "message",
                delayMilliseconds = 25
            },
            clock);
        var output = LinkOutput<string>(runtimeNode);
        var input = GetInput<string>(runtimeNode);

        // Capture the per-item delay registration before sending the item that arms it.
        var scheduled = clock.TimerScheduled;
        await input.Target.SendAsync("one").WaitAsync(TimeSpan.FromSeconds(30));
        input.Complete();

        // The item is held for 25ms; the delay stays pending until time advances.
        var (emitted, _) = await AdvanceUntilReceivedAsync(
            clock, output, scheduled, TimeSpan.FromMilliseconds(25));
        emitted.ShouldBe("one");
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Throttle_UsesConfiguredClock()
    {
        var clock = new TrackingFakeTimeProvider(new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        var runtimeNode = CreateNode(
            TimerComponentTypes.Throttle,
            new
            {
                inputType = "message",
                intervalMilliseconds = 30,
                emitFirstImmediately = false,
                boundedCapacity = 4
            },
            clock);
        var output = LinkOutput<string>(runtimeNode);
        var input = GetInput<string>(runtimeNode);

        // Capture the first interval timer's registration before sending the items.
        var scheduled = clock.TimerScheduled;
        await input.Target.SendAsync("one").WaitAsync(TimeSpan.FromSeconds(30));
        await input.Target.SendAsync("two").WaitAsync(TimeSpan.FromSeconds(30));
        input.Complete();

        // Each emission waits a 30ms interval; release them one at a time.
        var (first, scheduled2) = await AdvanceUntilReceivedAsync(
            clock, output, scheduled, TimeSpan.FromMilliseconds(30));
        var (second, _) = await AdvanceUntilReceivedAsync(
            clock, output, scheduled2, TimeSpan.FromMilliseconds(30));
        first.ShouldBe("one");
        second.ShouldBe("two");
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Debounce_UsesConfiguredClock()
    {
        var clock = new TrackingFakeTimeProvider(new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        var runtimeNode = CreateNode(
            TimerComponentTypes.Debounce,
            new
            {
                inputType = "message",
                quietPeriodMilliseconds = 40
            },
            clock);
        var output = LinkOutput<string>(runtimeNode);
        var input = GetInput<string>(runtimeNode);

        // Capture the quiet-period timer's registration before sending the item.
        var scheduled = clock.TimerScheduled;
        await input.Target.SendAsync("one").WaitAsync(TimeSpan.FromSeconds(30));

        // The debounce emits after the 40ms quiet period elapses with no further input.
        var (emitted, _) = await AdvanceUntilReceivedAsync(
            clock, output, scheduled, TimeSpan.FromMilliseconds(40));
        input.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        emitted.ShouldBe("one");
    }

    private static RuntimeNode CreateNode(
        NodeType type,
        object configuration,
        TrackingFakeTimeProvider clock)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterTimerComponents(options => options
                .UseClock(clock)
                .RegisterType<string>("message"));
        registry.TryGetFactory(type, out var factory).ShouldBeTrue();
        return factory(TimerTestHost.CreateContext(type, configuration));
    }

    private static InputPort<T> GetInput<T>(RuntimeNode runtimeNode)
        => runtimeNode.FindInput(new PortName(TimerComponentPorts.Input))
            .ShouldBeAssignableTo<InputPort<T>>()!;

    private static BufferBlock<T> LinkOutput<T>(RuntimeNode runtimeNode)
    {
        var output = new BufferBlock<T>();
        runtimeNode.FindOutput(new PortName(TimerComponentPorts.Output))!
            .TryLinkTo(
                new InputPort<T>(
                    new PortAddress("test", new NodeName("output"), new PortName("Input")),
                    output),
                propagateCompletion: true,
                out var error);
        error.ShouldBeNull();
        return output;
    }

    // FakeTimeProvider leaves Task.Delay pending until time advances, and the background
    // timer loops register their delays asynchronously. Awaiting the scheduled-timer
    // signal before each advance guarantees the loop has actually armed the delay we are
    // about to release, so the advance deterministically fires it instead of racing the
    // (possibly unregistered) timer.
    //
    // The signal for timer N+1 must be captured BEFORE the advance that fires timer N,
    // because that advance is what causes the loop to arm timer N+1. The returned
    // NextScheduled task is therefore captured before advancing and threaded into the
    // following call; the very first signal is captured by the test before the action
    // that arms the first delay.
    private static async Task<(T Value, Task NextScheduled)> AdvanceUntilReceivedAsync<T>(
        TrackingFakeTimeProvider clock,
        BufferBlock<T> output,
        Task scheduled,
        TimeSpan dueIn)
    {
        // Wait until the loop has registered the delay we are about to release.
        await scheduled.WaitAsync(TimeSpan.FromSeconds(30));
        // Capture the next registration before advancing; this advance is what arms it.
        var nextScheduled = clock.TimerScheduled;
        clock.Advance(dueIn);
        var value = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        return (value, nextScheduled);
    }
}
