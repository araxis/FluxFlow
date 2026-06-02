using FluxFlow.Components.Timers.Contracts;
using FluxFlow.Components.Timers.Timing;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
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
        var clock = new RecordingTimerClock(startedAt);
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

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var ticks = await DrainUntilCompletedAsync(output);
        ticks.Select(tick => tick.Sequence).ShouldBe([1, 2]);
        ticks.Select(tick => tick.StartedAt).Distinct().ShouldBe([startedAt]);
        ticks.Select(tick => tick.DueAt).ShouldBe(
        [
            startedAt.AddMilliseconds(50),
            startedAt.AddMilliseconds(150)
        ]);
        ticks.Select(tick => tick.Timestamp).ShouldBe(ticks.Select(tick => tick.DueAt));
        clock.Delays.ShouldBe([TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100)]);
    }

    [Fact]
    public async Task Schedule_UsesConfiguredClock()
    {
        var startedAt = new DateTimeOffset(2026, 6, 2, 11, 59, 59, TimeSpan.Zero);
        var clock = new RecordingTimerClock(startedAt);
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

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var tick = (await DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        var dueAt = new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero);
        tick.StartedAt.ShouldBe(startedAt);
        tick.DueAt.ShouldBe(dueAt);
        tick.Timestamp.ShouldBe(dueAt);
        clock.Delays.ShouldBe([TimeSpan.FromSeconds(1)]);
    }

    [Fact]
    public async Task Delay_UsesConfiguredClock()
    {
        var clock = new RecordingTimerClock(new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
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

        await input.Target.SendAsync("one").WaitAsync(TimeSpan.FromSeconds(5));
        input.Complete();

        (await DrainUntilCompletedAsync(output)).ShouldBe(["one"]);
        clock.Delays.ShouldBe([TimeSpan.FromMilliseconds(25)]);
    }

    [Fact]
    public async Task Throttle_UsesConfiguredClock()
    {
        var clock = new RecordingTimerClock(new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
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

        await input.Target.SendAsync("one").WaitAsync(TimeSpan.FromSeconds(5));
        await input.Target.SendAsync("two").WaitAsync(TimeSpan.FromSeconds(5));
        input.Complete();

        (await DrainUntilCompletedAsync(output)).ShouldBe(["one", "two"]);
        clock.Delays.ShouldBe([TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(30)]);
    }

    [Fact]
    public async Task Debounce_UsesConfiguredClock()
    {
        var clock = new RecordingTimerClock(new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
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

        await input.Target.SendAsync("one").WaitAsync(TimeSpan.FromSeconds(5));
        var emitted = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        input.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        emitted.ShouldBe("one");
        clock.Delays.ShouldBe([TimeSpan.FromMilliseconds(40)]);
    }

    private static RuntimeNode CreateNode(
        NodeType type,
        object configuration,
        RecordingTimerClock clock)
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

    private static async Task<List<T>> DrainUntilCompletedAsync<T>(
        BufferBlock<T> output)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var entries = new List<T>();
        while (await output.OutputAvailableAsync(cancellation.Token))
        {
            while (output.TryReceive(out var entry))
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    private sealed class RecordingTimerClock(DateTimeOffset utcNow) : ITimerClock
    {
        private readonly object _gate = new();
        private readonly List<TimeSpan> _delays = [];
        private DateTimeOffset _utcNow = utcNow;

        public DateTimeOffset UtcNow
        {
            get
            {
                lock (_gate)
                {
                    return _utcNow;
                }
            }
        }

        public IReadOnlyList<TimeSpan> Delays
        {
            get
            {
                lock (_gate)
                {
                    return _delays.ToArray();
                }
            }
        }

        public ValueTask DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (delay <= TimeSpan.Zero)
            {
                return ValueTask.CompletedTask;
            }

            lock (_gate)
            {
                _delays.Add(delay);
                _utcNow += delay;
            }

            return ValueTask.CompletedTask;
        }
    }
}
