using FluxFlow.Components.Sources.Contracts;
using FluxFlow.Components.Sources.Nodes;
using FluxFlow.Components.Sources.Options;
using FluxFlow.Nodes;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Sources.Tests;

public sealed class SequenceSourceNodeTests
{
    [Fact]
    public async Task Sequence_EmitsConfiguredValues()
    {
        await using var node = new SequenceSourceNode(new SequenceSourceOptions
        {
            Name = "numbers",
            Start = 10,
            Step = 5,
            Count = 3,
            BoundedCapacity = 8
        });
        var output = SourcesTestSink.Link(node.Output);

        await node.StartAsync();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var items = await SourcesTestSink.DrainUntilCompletedAsync(output);
        items.Select(message => message.Payload.Sequence).ShouldBe([1, 2, 3]);
        items.Select(message => message.Payload.Value).ShouldBe([10, 15, 20]);
        items.ShouldAllBe(message => message.Payload.Name == "numbers");
    }

    [Fact]
    public async Task Sequence_MintsAFreshCorrelationIdPerItem()
    {
        await using var node = new SequenceSourceNode(new SequenceSourceOptions { Count = 3 });
        var output = SourcesTestSink.Link(node.Output);

        await node.StartAsync();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var items = await SourcesTestSink.DrainUntilCompletedAsync(output);
        items.Count.ShouldBe(3);
        items.Select(message => message.CorrelationId).Distinct().Count().ShouldBe(3);
        items.ShouldAllBe(message => !message.CorrelationId.IsEmpty);
    }

    [Fact]
    public async Task Sequence_UsesConfiguredClockForTimingAndTimestamp()
    {
        var startInstant = new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero);
        var clock = new TrackingFakeTimeProvider(startInstant);
        await using var node = new SequenceSourceNode(
            new SequenceSourceOptions
            {
                InitialDelayMilliseconds = 10,
                IntervalMilliseconds = 25,
                Count = 2
            },
            clock);
        var output = SourcesTestSink.Link(node.Output);

        await node.StartAsync();

        // The node holds a 10ms initial delay then a 25ms interval between the two items.
        // FakeTimeProvider keeps each Task.Delay pending until time advances, so step the
        // clock forward until the run loop completes.
        await AdvanceUntilCompletedAsync(clock, node, TimeSpan.FromMilliseconds(25));

        var items = await SourcesTestSink.DrainUntilCompletedAsync(output);
        items.Count.ShouldBe(2);
        // Timestamps come from the configured clock's timeline, not wall-clock time.
        items.ShouldAllBe(message => message.Payload.Timestamp >= startInstant);
        items.ShouldAllBe(message => message.Payload.Timestamp <= clock.GetUtcNow());
    }

    [Fact]
    public async Task Sequence_HonorsInitialDelay()
    {
        var startInstant = new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero);
        var clock = new TrackingFakeTimeProvider(startInstant);
        await using var node = new SequenceSourceNode(
            new SequenceSourceOptions
            {
                InitialDelayMilliseconds = 40,
                Count = 1
            },
            clock);
        var output = SourcesTestSink.Link(node.Output);

        var scheduled = clock.TimerScheduled;
        await node.StartAsync();
        await scheduled.WaitAsync(TimeSpan.FromSeconds(30));
        // Nothing should be emitted before the 40ms initial delay elapses.
        output.TryReceive(out _).ShouldBeFalse();
        clock.Advance(TimeSpan.FromMilliseconds(40));
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var item = (await SourcesTestSink.DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        item.Payload.Timestamp.ShouldBe(startInstant.AddMilliseconds(40));
    }

    [Fact]
    public async Task Sequence_CompleteStopsSource()
    {
        await using var node = new SequenceSourceNode(new SequenceSourceOptions
        {
            Count = 100,
            IntervalMilliseconds = 10
        });
        var output = SourcesTestSink.Link(node.Output);

        await node.StartAsync();
        await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        node.Completion.IsFaulted.ShouldBeFalse();

        await SourcesTestSink.DrainUntilCompletedAsync(output);
    }

    [Fact]
    public async Task Sequence_CompleteBeforeStartCompletesOutput()
    {
        await using var node = new SequenceSourceNode(new SequenceSourceOptions { Count = 1 });
        var output = SourcesTestSink.Link(node.Output);

        node.Complete();
        await node.DisposeAsync();

        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        await output.Completion.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Sequence_EmitsLifecycleEvents()
    {
        await using var node = new SequenceSourceNode(new SequenceSourceOptions { Count = 1 });
        var output = SourcesTestSink.Link(node.Output);
        var events = SourcesTestSink.Link(node.Events);

        await node.StartAsync();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await SourcesTestSink.DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        var names = (await SourcesTestSink.DrainUntilCompletedAsync(events))
            .Select(flowEvent => flowEvent.Name)
            .ToArray();
        names.ShouldContain(SequenceSourceNode.Started);
        names.ShouldContain(SequenceSourceNode.Emitted);
        names.ShouldContain(SequenceSourceNode.Completed);
    }

    [Fact]
    public void Sequence_RejectsInvalidCount()
        => Should.Throw<ArgumentOutOfRangeException>(
                () => new SequenceSourceNode(new SequenceSourceOptions { Count = 0 }))
            .Message.ShouldContain("count");

    [Fact]
    public void Sequence_RejectsInvalidStep()
        => Should.Throw<ArgumentOutOfRangeException>(
                () => new SequenceSourceNode(new SequenceSourceOptions { Step = 0 }))
            .Message.ShouldContain("step");

    [Fact]
    public void Sequence_RejectsInvalidCapacity()
        => Should.Throw<ArgumentOutOfRangeException>(
                () => new SequenceSourceNode(new SequenceSourceOptions { BoundedCapacity = 0 }))
            .Message.ShouldContain("boundedCapacity");

    [Theory]
    [InlineData("initialDelayMilliseconds")]
    [InlineData("intervalMilliseconds")]
    public void Sequence_RejectsNegativeTiming(string optionName)
    {
        var options = optionName == "initialDelayMilliseconds"
            ? new SequenceSourceOptions { InitialDelayMilliseconds = -1 }
            : new SequenceSourceOptions { IntervalMilliseconds = -1 };

        Should.Throw<ArgumentOutOfRangeException>(
                () => new SequenceSourceNode(options))
            .Message.ShouldContain(optionName);
    }

    [Fact]
    public void Sequence_RejectsNullOptions()
        => Should.Throw<ArgumentNullException>(() => new SequenceSourceNode(null!));

    // FakeTimeProvider leaves each Task.Delay pending until time advances, and the run
    // loop registers its delays asynchronously. This drains them deterministically:
    // advance exactly once for each newly created timer, gating on the wrapper's created
    // count (and its registration signal) rather than polling with sleeps. Counting
    // created timers avoids the lost-wakeup where a timer is armed before the test looks.
    private static async Task AdvanceUntilCompletedAsync(
        TrackingFakeTimeProvider clock,
        IFlowNode node,
        TimeSpan step)
    {
        var fired = 0;
        while (!node.Completion.IsCompleted)
        {
            // Capture the next-timer registration signal BEFORE reading the count so a
            // timer armed in the gap is not a lost-wakeup.
            var scheduled = clock.TimerScheduled;

            if (clock.CreatedTimerCount > fired)
            {
                // A delay is armed (or already was) but not yet released: fire it.
                clock.Advance(step);
                fired++;
                continue;
            }

            // No unfired timer yet: wait until the loop arms the next one or completes.
            await Task.WhenAny(scheduled, node.Completion)
                .WaitAsync(TimeSpan.FromSeconds(30));
        }

        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
