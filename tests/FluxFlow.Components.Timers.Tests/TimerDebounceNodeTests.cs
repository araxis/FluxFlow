using FluxFlow.Components.Timers.Nodes;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Nodes;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Timers.Tests;

public sealed class TimerDebounceNodeTests
{
    [Fact]
    public async Task Debounce_EmitsLatestPendingOnCompletion_PreservingCorrelation()
    {
        await using var node = new TimerDebounceNode<InputMessage>(
            new TimerDebounceSettings
            {
                Name = "quiet",
                QuietPeriod = TimeSpan.FromMilliseconds(40),
                BoundedCapacity = 4
            });
        var output = TimerTestSink.Link(node.Output);
        var first = FlowMessage.Create(new InputMessage("one"));
        var latest = FlowMessage.Create(new InputMessage("two"));

        await node.Input.SendAsync(first);
        await node.Input.SendAsync(latest);
        // Completing the input short-circuits the quiet wait and flushes only the latest.
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var emitted = (await TimerTestSink.DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        emitted.Payload.Value.ShouldBe("two");
        emitted.CorrelationId.ShouldBe(latest.CorrelationId);
    }

    [Fact]
    public async Task Debounce_EmitsAfterQuietPeriodElapses()
    {
        var clock = new TrackingFakeTimeProvider(new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        await using var node = new TimerDebounceNode<string>(
            new TimerDebounceSettings { QuietPeriod = TimeSpan.FromMilliseconds(40) },
            clock);
        var output = TimerTestSink.Link(node.Output);

        var scheduled = clock.TimerScheduled;
        await node.Input.SendAsync(FlowMessage.Create("one"));
        await scheduled.WaitAsync(TimeSpan.FromSeconds(30));
        // The item is held until the quiet period elapses with no further input.
        output.TryReceive(out _).ShouldBeFalse();
        clock.Advance(TimeSpan.FromMilliseconds(40));

        var emitted = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        emitted.Payload.ShouldBe("one");
    }

    [Fact]
    public async Task Debounce_FlushesPendingInputOnCompletion()
    {
        // A long quiet period would never elapse on its own; completing the input must
        // flush the single pending item promptly.
        await using var node = new TimerDebounceNode<string>(
            new TimerDebounceSettings { QuietPeriod = TimeSpan.FromSeconds(1000) });
        var output = TimerTestSink.Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create("one"));
        node.Complete();

        var value = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        value.Payload.ShouldBe("one");
    }

    [Fact]
    public async Task Debounce_EmitsLatestPerQuietWindow()
    {
        var clock = new TrackingFakeTimeProvider(new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        await using var node = new TimerDebounceNode<int>(
            new TimerDebounceSettings
            {
                QuietPeriod = TimeSpan.FromMilliseconds(25),
                BoundedCapacity = 8
            },
            clock);
        var output = TimerTestSink.Link(node.Output);

        // First window: 1 then 2 arrive; only the latest (2) survives the quiet period.
        // Send each item and wait until its quiet-period timer is armed before advancing,
        // so both items have been observed (the later one supersedes the earlier).
        var scheduled1 = clock.TimerScheduled;
        await node.Input.SendAsync(FlowMessage.Create(1));
        await scheduled1.WaitAsync(TimeSpan.FromSeconds(30));
        var scheduled2 = clock.TimerScheduled;
        await node.Input.SendAsync(FlowMessage.Create(2));
        await scheduled2.WaitAsync(TimeSpan.FromSeconds(30));
        clock.Advance(TimeSpan.FromMilliseconds(25));
        var first = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));

        // Second window: a single later item (3) is emitted in its own window.
        var scheduled3 = clock.TimerScheduled;
        await node.Input.SendAsync(FlowMessage.Create(3));
        await scheduled3.WaitAsync(TimeSpan.FromSeconds(30));
        clock.Advance(TimeSpan.FromMilliseconds(25));
        var second = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));

        first.Payload.ShouldBe(2);
        second.Payload.ShouldBe(3);
    }

    [Fact]
    public async Task Debounce_EmitsEvents()
    {
        await using var node = new TimerDebounceNode<string>(
            new TimerDebounceSettings { QuietPeriod = TimeSpan.FromMilliseconds(1) });
        var output = TimerTestSink.Link(node.Output);
        var events = TimerTestSink.Link(node.Events);

        await node.Input.SendAsync(FlowMessage.Create("hello"));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await TimerTestSink.DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        var flowEvent = (await TimerTestSink.DrainUntilCompletedAsync(events))
            .ShouldHaveSingleItem();
        flowEvent.Name.ShouldBe(TimerDebounceNode<string>.Emitted);
        flowEvent.Attributes["inputType"].ShouldBe(nameof(String));
        flowEvent.Attributes["sequence"].ShouldBe(1L);
    }

    [Fact]
    public async Task Debounce_DisposeFlushesAndCompletesOutput()
    {
        await using var node = new TimerDebounceNode<string>(
            new TimerDebounceSettings { QuietPeriod = TimeSpan.FromSeconds(1000) });
        var output = TimerTestSink.Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create("one"));
        await node.DisposeAsync();

        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        (await TimerTestSink.DrainUntilCompletedAsync(output))
            .Select(message => message.Payload)
            .ShouldBe(["one"]);
    }

    [Fact]
    public async Task Debounce_DisposeAfterFaultDoesNotThrow()
    {
        var node = new TimerDebounceNode<string>(
            new TimerDebounceSettings { QuietPeriod = TimeSpan.FromMilliseconds(1) });
        TimerTestSink.Link(node.Output);

        node.Fault(new InvalidOperationException("boom"));
        await node.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));

        await Should.ThrowAsync<InvalidOperationException>(() => node.Completion);
    }

    [Fact]
    public void Debounce_RejectsNonPositiveQuietPeriod()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new TimerDebounceNode<string>(
                new TimerDebounceSettings { QuietPeriod = TimeSpan.Zero }))
            .Message.ShouldContain("QuietPeriod");

    [Fact]
    public void Debounce_RejectsInvalidBoundedCapacity()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new TimerDebounceNode<string>(
                new TimerDebounceSettings
                {
                    QuietPeriod = TimeSpan.FromMilliseconds(1),
                    BoundedCapacity = 0
                }))
            .Message.ShouldContain("BoundedCapacity");

    [Fact]
    public void Debounce_RejectsNullSettings()
        => Should.Throw<ArgumentNullException>(() => new TimerDebounceNode<string>(null!));

    private sealed record InputMessage(string Value);
}
