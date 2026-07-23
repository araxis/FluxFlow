using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Timers.Diagnostics;
using FluxFlow.Components.Timers.Nodes;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Timers.Tests;

public sealed class TimerThrottleNodeTests
{
    [Fact]
    public async Task Throttle_emits_first_input_immediately_with_lineage()
    {
        var clock = new TrackingFakeTimeProvider(
            new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        await using var node = new TimerThrottleNode(
            new TimerThrottleSettings { Interval = TimeSpan.FromSeconds(1) },
            clock);
        var output = TimerTestSink.Link(node.Output);
        var message = FlowMessage.Create(FlowValue.From("one"));

        await node.Input.SendAsync(message);

        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        result.Payload.Value.ShouldBe(message.Payload);
        result.CorrelationId.ShouldBe(message.CorrelationId);
        result.TraceId.ShouldBe(message.TraceId);
        result.CausationId.ShouldBe(message.MessageId);
    }

    [Fact]
    public async Task Throttle_spaces_later_inputs_by_interval()
    {
        var clock = new TrackingFakeTimeProvider(
            new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        await using var node = new TimerThrottleNode(
            new TimerThrottleSettings
            {
                Name = "rate",
                Interval = TimeSpan.FromMilliseconds(45),
                BoundedCapacity = 4
            },
            clock);
        var output = TimerTestSink.Link(node.Output);

        var scheduled = clock.TimerScheduled;
        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From("one")));
        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From("two")));

        var first = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await scheduled.WaitAsync(TimeSpan.FromSeconds(30));
        output.TryReceive(out _).ShouldBeFalse();
        clock.Advance(TimeSpan.FromMilliseconds(45));
        var second = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));

        first.Payload.Value!.GetString().ShouldBe("one");
        second.Payload.Value!.GetString().ShouldBe("two");
    }

    [Fact]
    public async Task Throttle_can_delay_first_input()
    {
        var clock = new TrackingFakeTimeProvider(
            new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        await using var node = new TimerThrottleNode(
            new TimerThrottleSettings
            {
                Interval = TimeSpan.FromMilliseconds(35),
                EmitFirstImmediately = false
            },
            clock);
        var output = TimerTestSink.Link(node.Output);

        var scheduled = clock.TimerScheduled;
        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From("hello")));
        await scheduled.WaitAsync(TimeSpan.FromSeconds(30));
        output.TryReceive(out _).ShouldBeFalse();
        clock.Advance(TimeSpan.FromMilliseconds(35));

        (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30)))
            .Payload.Value!.GetString().ShouldBe("hello");
    }

    [Fact]
    public async Task Throttle_preserves_order()
    {
        await using var node = new TimerThrottleNode(
            new TimerThrottleSettings
            {
                Interval = TimeSpan.FromMilliseconds(1),
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
    public async Task Throttle_emits_result_events()
    {
        await using var node = new TimerThrottleNode(
            new TimerThrottleSettings { Interval = TimeSpan.FromMilliseconds(1) });
        var output = TimerTestSink.Link(node.Output);
        var events = TimerTestSink.Link(node.Events);

        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From("hello")));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await TimerTestSink.DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        var flowEvent = (await TimerTestSink.DrainUntilCompletedAsync(events))
            .ShouldHaveSingleItem();
        flowEvent.Name.ShouldBe(TimerDiagnosticNames.ThrottleEmitted);
        flowEvent.Attributes["resultKind"].ShouldBe(TimerResultKinds.Throttled);
        flowEvent.Attributes["nodeType"].ShouldBe("timer.throttle");
    }

    [Fact]
    public async Task Throttle_dispose_drains_and_completes_output()
    {
        await using var node = new TimerThrottleNode(
            new TimerThrottleSettings { Interval = TimeSpan.FromMilliseconds(1) });
        var output = TimerTestSink.Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From("one")));
        await node.DisposeAsync();

        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        (await TimerTestSink.DrainUntilCompletedAsync(output))
            .Select(message => message.Payload.Value!.GetString())
            .ShouldBe(["one"]);
    }

    [Fact]
    public async Task Throttle_dispose_after_fault_does_not_throw()
    {
        var node = new TimerThrottleNode(
            new TimerThrottleSettings { Interval = TimeSpan.FromMilliseconds(1) });
        TimerTestSink.Link(node.Output);

        node.Fault(new InvalidOperationException("boom"));
        await node.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));

        await Should.ThrowAsync<InvalidOperationException>(() => node.Completion);
    }

    [Fact]
    public void Throttle_rejects_non_positive_interval()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new TimerThrottleNode(
                new TimerThrottleSettings { Interval = TimeSpan.Zero }))
            .Message.ShouldContain("Interval");

    [Fact]
    public void Throttle_rejects_invalid_bounded_capacity()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new TimerThrottleNode(
                new TimerThrottleSettings
                {
                    Interval = TimeSpan.FromMilliseconds(1),
                    BoundedCapacity = 0
                }))
            .Message.ShouldContain("BoundedCapacity");

    [Fact]
    public void Throttle_rejects_null_settings()
        => Should.Throw<ArgumentNullException>(() => new TimerThrottleNode(null!));
}
