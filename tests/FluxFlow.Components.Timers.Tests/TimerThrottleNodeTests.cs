using FluxFlow.Components.Timers.Nodes;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Nodes;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Timers.Tests;

public sealed class TimerThrottleNodeTests
{
    [Fact]
    public async Task Throttle_EmitsFirstInputImmediatelyByDefault_PreservingCorrelation()
    {
        var clock = new TrackingFakeTimeProvider(new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        await using var node = new TimerThrottleNode<string>(
            new TimerThrottleSettings { Interval = TimeSpan.FromMilliseconds(1000) },
            clock);
        var output = TimerTestSink.Link(node.Output);
        var message = FlowMessage.Create("one");

        await node.Input.SendAsync(message);

        // EmitFirstImmediately is the default, so the first item needs no clock advance.
        var value = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        value.Payload.ShouldBe("one");
        value.CorrelationId.ShouldBe(message.CorrelationId);
    }

    [Fact]
    public async Task Throttle_SpacesLaterInputsByInterval()
    {
        var clock = new TrackingFakeTimeProvider(new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        await using var node = new TimerThrottleNode<string>(
            new TimerThrottleSettings
            {
                Name = "rate",
                Interval = TimeSpan.FromMilliseconds(45),
                BoundedCapacity = 4
            },
            clock);
        var output = TimerTestSink.Link(node.Output);

        // The first item emits immediately (no timer); the second arms a one-interval
        // timer, so capture that registration before sending the burst that arms it.
        var scheduled = clock.TimerScheduled;
        await node.Input.SendAsync(FlowMessage.Create("one"));
        await node.Input.SendAsync(FlowMessage.Create("two"));

        var first = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await scheduled.WaitAsync(TimeSpan.FromSeconds(30));
        output.TryReceive(out _).ShouldBeFalse();
        clock.Advance(TimeSpan.FromMilliseconds(45));
        var second = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));

        first.Payload.ShouldBe("one");
        second.Payload.ShouldBe("two");
    }

    [Fact]
    public async Task Throttle_CanDelayFirstInput()
    {
        var clock = new TrackingFakeTimeProvider(new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        await using var node = new TimerThrottleNode<string>(
            new TimerThrottleSettings
            {
                Interval = TimeSpan.FromMilliseconds(35),
                EmitFirstImmediately = false
            },
            clock);
        var output = TimerTestSink.Link(node.Output);

        var scheduled = clock.TimerScheduled;
        await node.Input.SendAsync(FlowMessage.Create("hello"));
        await scheduled.WaitAsync(TimeSpan.FromSeconds(30));
        output.TryReceive(out _).ShouldBeFalse();
        clock.Advance(TimeSpan.FromMilliseconds(35));

        var value = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        value.Payload.ShouldBe("hello");
    }

    [Fact]
    public async Task Throttle_PreservesOrder()
    {
        await using var node = new TimerThrottleNode<int>(
            new TimerThrottleSettings
            {
                Interval = TimeSpan.FromMilliseconds(1),
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
    public async Task Throttle_EmitsEvents()
    {
        await using var node = new TimerThrottleNode<string>(
            new TimerThrottleSettings { Interval = TimeSpan.FromMilliseconds(1) });
        var output = TimerTestSink.Link(node.Output);
        var events = TimerTestSink.Link(node.Events);

        await node.Input.SendAsync(FlowMessage.Create("hello"));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await TimerTestSink.DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        var flowEvent = (await TimerTestSink.DrainUntilCompletedAsync(events))
            .ShouldHaveSingleItem();
        flowEvent.Name.ShouldBe(TimerThrottleNode<string>.Emitted);
        flowEvent.Attributes["inputType"].ShouldBe(nameof(String));
        flowEvent.Attributes["sequence"].ShouldBe(1L);
    }

    [Fact]
    public async Task Throttle_DisposeDrainsAndCompletesOutput()
    {
        await using var node = new TimerThrottleNode<string>(
            new TimerThrottleSettings { Interval = TimeSpan.FromMilliseconds(1) });
        var output = TimerTestSink.Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create("one"));
        await node.DisposeAsync();

        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        (await TimerTestSink.DrainUntilCompletedAsync(output))
            .Select(message => message.Payload)
            .ShouldBe(["one"]);
    }

    [Fact]
    public async Task Throttle_DisposeAfterFaultDoesNotThrow()
    {
        var node = new TimerThrottleNode<string>(
            new TimerThrottleSettings { Interval = TimeSpan.FromMilliseconds(1) });
        TimerTestSink.Link(node.Output);

        node.Fault(new InvalidOperationException("boom"));
        await node.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));

        await Should.ThrowAsync<InvalidOperationException>(() => node.Completion);
    }

    [Fact]
    public void Throttle_RejectsNonPositiveInterval()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new TimerThrottleNode<string>(
                new TimerThrottleSettings { Interval = TimeSpan.Zero }))
            .Message.ShouldContain("Interval");

    [Fact]
    public void Throttle_RejectsInvalidBoundedCapacity()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new TimerThrottleNode<string>(
                new TimerThrottleSettings
                {
                    Interval = TimeSpan.FromMilliseconds(1),
                    BoundedCapacity = 0
                }));

    [Fact]
    public void Throttle_RejectsNullSettings()
        => Should.Throw<ArgumentNullException>(() => new TimerThrottleNode<string>(null!));
}
