using FluxFlow.Components.Timers;
using FluxFlow.Components.Timers.Nodes;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Nodes;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Timers.Tests;

public sealed class TimerDelayNodeTests
{
    [Fact]
    public async Task Delay_EmitsInputAfterConfiguredDelay_PreservingCorrelation()
    {
        var clock = new TrackingFakeTimeProvider(new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        await using var node = new TimerDelayNode<InputMessage>(
            new TimerDelaySettings
            {
                Name = "hold",
                Delay = TimeSpan.FromMilliseconds(35),
                BoundedCapacity = 4
            },
            clock);
        var output = TimerTestSink.Link(node.Output);
        var message = FlowMessage.Create(new InputMessage("one"));

        var scheduled = clock.TimerScheduled;
        await node.Input.SendAsync(message);
        await scheduled.WaitAsync(TimeSpan.FromSeconds(30));
        // Nothing should be emitted before the delay elapses.
        output.TryReceive(out _).ShouldBeFalse();
        clock.Advance(TimeSpan.FromMilliseconds(35));

        var delayed = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        delayed.Payload.ShouldBe(message.Payload);
        delayed.CorrelationId.ShouldBe(message.CorrelationId);
    }

    [Fact]
    public async Task Delay_PreservesOrder()
    {
        await using var node = new TimerDelayNode<int>(
            new TimerDelaySettings
            {
                Delay = TimeSpan.FromMilliseconds(1),
                BoundedCapacity = 8
            });
        var output = TimerTestSink.Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(1));
        await node.Input.SendAsync(FlowMessage.Create(2));
        await node.Input.SendAsync(FlowMessage.Create(3));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await TimerTestSink.DrainUntilCompletedAsync(output))
            .Select(message => message.Payload)
            .ShouldBe([1, 2, 3]);
    }

    [Fact]
    public async Task Delay_BurstSharesOneConstantOffsetFromArrival()
    {
        var clock = new TrackingFakeTimeProvider(new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        await using var node = new TimerDelayNode<int>(
            new TimerDelaySettings
            {
                Delay = TimeSpan.FromMilliseconds(40),
                BoundedCapacity = 8
            },
            clock);
        var output = TimerTestSink.Link(node.Output);

        // The burst arrives at the same instant, so all three share one due time
        // (arrival + 40ms) — only the first item arms a timer; the rest are already due.
        var scheduled = clock.TimerScheduled;
        await node.Input.SendAsync(FlowMessage.Create(1));
        await node.Input.SendAsync(FlowMessage.Create(2));
        await node.Input.SendAsync(FlowMessage.Create(3));
        await scheduled.WaitAsync(TimeSpan.FromSeconds(30));
        output.TryReceive(out _).ShouldBeFalse();

        // A single advance by one Delay releases the whole burst (constant offset), rather
        // than accumulating one delay per item (which would need three advances).
        clock.Advance(TimeSpan.FromMilliseconds(40));

        var first = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var second = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var third = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        new[] { first.Payload, second.Payload, third.Payload }.ShouldBe([1, 2, 3]);
    }

    [Fact]
    public async Task Delay_ZeroDelayPassesThroughImmediately()
    {
        await using var node = new TimerDelayNode<string>(
            new TimerDelaySettings { Delay = TimeSpan.Zero });
        var output = TimerTestSink.Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create("hello"));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await TimerTestSink.DrainUntilCompletedAsync(output))
            .Select(message => message.Payload)
            .ShouldBe(["hello"]);
    }

    [Fact]
    public async Task Delay_EmitsEvents()
    {
        await using var node = new TimerDelayNode<string>(
            new TimerDelaySettings { Delay = TimeSpan.Zero });
        var output = TimerTestSink.Link(node.Output);
        var events = TimerTestSink.Link(node.Events);

        await node.Input.SendAsync(FlowMessage.Create("hello"));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await TimerTestSink.DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        var flowEvent = (await TimerTestSink.DrainUntilCompletedAsync(events))
            .ShouldHaveSingleItem();
        flowEvent.Name.ShouldBe(TimerDelayNode<string>.Emitted);
        flowEvent.Attributes["inputType"].ShouldBe(nameof(String));
    }

    [Fact]
    public async Task Delay_DisposeDrainsAndCompletesOutput()
    {
        await using var node = new TimerDelayNode<string>(
            new TimerDelaySettings { Delay = TimeSpan.Zero });
        var output = TimerTestSink.Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create("one"));
        await node.DisposeAsync();

        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        (await TimerTestSink.DrainUntilCompletedAsync(output))
            .Select(message => message.Payload)
            .ShouldBe(["one"]);
    }

    [Fact]
    public async Task Delay_ErrorsPortReceivesPerMessageFailures()
    {
        var clock = new ThrowOnFirstTimerProvider();
        await using var node = new TimerDelayNode<string>(
            new TimerDelaySettings { Delay = TimeSpan.FromMilliseconds(5) },
            clock);
        var output = TimerTestSink.Link(node.Output);
        var errors = TimerTestSink.Link(node.Errors);

        var bad = FlowMessage.Create("bad");
        await node.Input.SendAsync(bad);
        await node.Input.SendAsync(FlowMessage.Create("good"));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Code.ShouldBe(TimerErrorCodes.DelayFailed);
        error.CorrelationId.ShouldBe(bad.CorrelationId);
        (await TimerTestSink.DrainUntilCompletedAsync(output))
            .Select(message => message.Payload)
            .ShouldBe(["good"]);
    }

    [Fact]
    public async Task Delay_DisposeAfterFaultDoesNotThrow()
    {
        var node = new TimerDelayNode<string>(
            new TimerDelaySettings { Delay = TimeSpan.FromMilliseconds(1) });
        TimerTestSink.Link(node.Output);

        node.Fault(new InvalidOperationException("boom"));
        await node.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));

        await Should.ThrowAsync<InvalidOperationException>(() => node.Completion);
    }

    [Fact]
    public void Delay_RejectsNegativeDelay()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new TimerDelayNode<string>(
                new TimerDelaySettings { Delay = TimeSpan.FromMilliseconds(-1) }))
            .Message.ShouldContain("Delay");

    [Fact]
    public void Delay_RejectsInvalidBoundedCapacity()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new TimerDelayNode<string>(
                new TimerDelaySettings
                {
                    Delay = TimeSpan.FromMilliseconds(1),
                    BoundedCapacity = 0
                }));

    [Fact]
    public void Delay_RejectsNullSettings()
        => Should.Throw<ArgumentNullException>(() => new TimerDelayNode<string>(null!));

    private sealed record InputMessage(string Value);

    // FakeTimeProvider cannot be told to throw from a delay, so this bespoke provider
    // throws from the timer path exactly once to exercise per-message fault handling.
    // GetUtcNow returns a fixed instant so the node sees a positive remaining delay and
    // actually reaches Task.Delay (which creates a timer).
    private sealed class ThrowOnFirstTimerProvider : TimeProvider
    {
        private int _calls;

        public override DateTimeOffset GetUtcNow()
            => new(2026, 6, 2, 12, 0, 0, TimeSpan.Zero);

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                throw new InvalidOperationException("clock failed");
            }

            return System.CreateTimer(callback, state, dueTime, period);
        }
    }
}
