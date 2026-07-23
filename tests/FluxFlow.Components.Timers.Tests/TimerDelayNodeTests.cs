using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Timers.Diagnostics;
using FluxFlow.Components.Timers.Nodes;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Timers.Tests;

public sealed class TimerDelayNodeTests
{
    [Fact]
    public async Task Delay_emits_input_after_configured_delay_with_lineage()
    {
        var clock = new TrackingFakeTimeProvider(
            new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        await using var node = new TimerDelayNode(
            new TimerDelaySettings
            {
                Name = "hold",
                Delay = TimeSpan.FromMilliseconds(35),
                BoundedCapacity = 4
            },
            clock);
        var output = TimerTestSink.Link(node.Output);
        var message = FlowMessage.Create(FlowValue.From("one"));

        var scheduled = clock.TimerScheduled;
        await node.Input.SendAsync(message);
        await scheduled.WaitAsync(TimeSpan.FromSeconds(30));
        output.TryReceive(out _).ShouldBeFalse();
        clock.Advance(TimeSpan.FromMilliseconds(35));

        var delayed = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        delayed.Payload.Kind.ShouldBe(TimerResultKinds.Delayed);
        delayed.Payload.Value.ShouldBe(message.Payload);
        delayed.CorrelationId.ShouldBe(message.CorrelationId);
        delayed.TraceId.ShouldBe(message.TraceId);
        delayed.CausationId.ShouldBe(message.MessageId);
    }

    [Fact]
    public async Task Delay_preserves_order()
    {
        await using var node = new TimerDelayNode(
            new TimerDelaySettings
            {
                Delay = TimeSpan.FromMilliseconds(1),
                BoundedCapacity = 8
            });
        var output = TimerTestSink.Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From(1)));
        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From(2)));
        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From(3)));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await TimerTestSink.DrainUntilCompletedAsync(output))
            .Select(message => message.Payload.Value!.GetInteger())
            .ShouldBe([1L, 2L, 3L]);
    }

    [Fact]
    public async Task Delay_burst_shares_one_constant_offset_from_arrival()
    {
        var clock = new TrackingFakeTimeProvider(
            new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        await using var node = new TimerDelayNode(
            new TimerDelaySettings
            {
                Delay = TimeSpan.FromMilliseconds(40),
                BoundedCapacity = 8
            },
            clock);
        var output = TimerTestSink.Link(node.Output);

        var scheduled = clock.TimerScheduled;
        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From(1)));
        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From(2)));
        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From(3)));
        await scheduled.WaitAsync(TimeSpan.FromSeconds(30));
        output.TryReceive(out _).ShouldBeFalse();
        clock.Advance(TimeSpan.FromMilliseconds(40));

        var first = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var second = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var third = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        new[]
        {
            first.Payload.Value!.GetInteger(),
            second.Payload.Value!.GetInteger(),
            third.Payload.Value!.GetInteger()
        }.ShouldBe([1L, 2L, 3L]);
    }

    [Fact]
    public async Task Delay_zero_delay_passes_through_immediately()
    {
        await using var node = new TimerDelayNode(
            new TimerDelaySettings { Delay = TimeSpan.Zero });
        var output = TimerTestSink.Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From("hello")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await TimerTestSink.DrainUntilCompletedAsync(output))
            .Select(message => message.Payload.Value!.GetString())
            .ShouldBe(["hello"]);
    }

    [Fact]
    public async Task Delay_emits_result_event()
    {
        await using var node = new TimerDelayNode(
            new TimerDelaySettings { Delay = TimeSpan.Zero });
        var output = TimerTestSink.Link(node.Output);
        var events = TimerTestSink.Link(node.Events);

        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From("hello")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await TimerTestSink.DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        var flowEvent = (await TimerTestSink.DrainUntilCompletedAsync(events))
            .ShouldHaveSingleItem();
        flowEvent.Name.ShouldBe(TimerDiagnosticNames.DelayEmitted);
        flowEvent.Attributes["resultKind"].ShouldBe(TimerResultKinds.Delayed);
        flowEvent.Attributes["nodeType"].ShouldBe("timer.delay");
    }

    [Fact]
    public async Task Delay_dispose_drains_and_completes_output()
    {
        await using var node = new TimerDelayNode(
            new TimerDelaySettings { Delay = TimeSpan.Zero });
        var output = TimerTestSink.Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From("one")));
        await node.DisposeAsync();

        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        (await TimerTestSink.DrainUntilCompletedAsync(output))
            .Select(message => message.Payload.Value!.GetString())
            .ShouldBe(["one"]);
    }

    [Fact]
    public async Task Delay_failure_is_a_normal_result_and_later_input_continues()
    {
        await using var node = new TimerDelayNode(
            new TimerDelaySettings { Delay = TimeSpan.FromMilliseconds(5) },
            new ThrowOnFirstTimerProvider());
        var output = TimerTestSink.Link(node.Output);
        var bad = FlowMessage.Create(FlowValue.From("bad"));

        await node.Input.SendAsync(bad);
        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From("good")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var results = await TimerTestSink.DrainUntilCompletedAsync(output);
        results.Count.ShouldBe(2);
        results[0].Payload.Error!.Code.ShouldBe(TimerErrorCodeNames.DelayFailed);
        results[0].CorrelationId.ShouldBe(bad.CorrelationId);
        results[1].Payload.Value!.GetString().ShouldBe("good");
    }

    [Fact]
    public async Task Delay_dispose_after_fault_does_not_throw()
    {
        var node = new TimerDelayNode(
            new TimerDelaySettings { Delay = TimeSpan.FromMilliseconds(1) });
        TimerTestSink.Link(node.Output);

        node.Fault(new InvalidOperationException("boom"));
        await node.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));

        await Should.ThrowAsync<InvalidOperationException>(() => node.Completion);
    }

    [Fact]
    public void Delay_rejects_negative_delay()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new TimerDelayNode(
                new TimerDelaySettings { Delay = TimeSpan.FromMilliseconds(-1) }))
            .Message.ShouldContain("Delay");

    [Fact]
    public void Delay_rejects_invalid_bounded_capacity()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new TimerDelayNode(
                new TimerDelaySettings
                {
                    Delay = TimeSpan.FromMilliseconds(1),
                    BoundedCapacity = 0
                }))
            .Message.ShouldContain("BoundedCapacity");

    [Fact]
    public void Delay_rejects_null_settings()
        => Should.Throw<ArgumentNullException>(() => new TimerDelayNode(null!));

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
                throw new InvalidOperationException("clock failed");

            return System.CreateTimer(callback, state, dueTime, period);
        }
    }
}
