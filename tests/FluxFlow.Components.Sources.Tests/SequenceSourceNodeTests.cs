using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Sources.Nodes;
using FluxFlow.Components.Sources.Options;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Sources.Tests;

public sealed class SequenceSourceNodeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Sequence_emits_configured_immutable_values()
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
        await node.Completion.WaitAsync(Timeout);

        var items = await SourcesTestSink.DrainUntilCompletedAsync(output);
        items.Select(message => message.Value.Sequence)
            .ShouldBe([1, 2, 3]);
        items.Select(message => message.Value.Value)
            .ShouldBe([10, 15, 20]);
        items.ShouldAllBe(message =>
            message.Value.Name == "numbers");
    }

    [Fact]
    public async Task Sequence_mints_a_fresh_identity_per_item()
    {
        await using var node = new SequenceSourceNode(
            new SequenceSourceOptions { Count = 3 });
        var output = SourcesTestSink.Link(node.Output);

        await node.StartAsync();
        await node.Completion.WaitAsync(Timeout);

        var items = await SourcesTestSink.DrainUntilCompletedAsync(output);
        items.Count.ShouldBe(3);
        items.Select(message => message.MessageId).Distinct().Count().ShouldBe(3);
        items.ShouldAllBe(message => !message.TraceId.IsEmpty);
    }

    [Fact]
    public async Task Sequence_uses_configured_clock_for_timing_and_timestamp()
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
        await AdvanceUntilCompletedAsync(clock, node, TimeSpan.FromMilliseconds(25));

        var items = await SourcesTestSink.DrainUntilCompletedAsync(output);
        items.Count.ShouldBe(2);
        items.ShouldAllBe(message =>
            message.Value.Timestamp >= startInstant);
        items.ShouldAllBe(message =>
            message.Value.Timestamp <= clock.GetUtcNow());
    }

    [Fact]
    public async Task Sequence_honors_initial_delay()
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
        await scheduled.WaitAsync(Timeout);
        output.TryReceive(out _).ShouldBeFalse();
        clock.Advance(TimeSpan.FromMilliseconds(40));
        await node.Completion.WaitAsync(Timeout);

        var item = (await SourcesTestSink.DrainUntilCompletedAsync(output))
            .ShouldHaveSingleItem();
        item.Value.Timestamp
            .ShouldBe(startInstant.AddMilliseconds(40));
    }

    [Fact]
    public async Task Sequence_complete_stops_the_source()
    {
        await using var node = new SequenceSourceNode(new SequenceSourceOptions
        {
            Count = 100,
            IntervalMilliseconds = 10
        });
        var output = SourcesTestSink.Link(node.Output);

        await node.StartAsync();
        await output.ReceiveAsync().WaitAsync(Timeout);
        node.Complete();
        await node.Completion.WaitAsync(Timeout);

        node.Completion.IsFaulted.ShouldBeFalse();
        await SourcesTestSink.DrainUntilCompletedAsync(output);
    }

    [Fact]
    public async Task Sequence_complete_before_start_settles_output()
    {
        await using var node = new SequenceSourceNode(
            new SequenceSourceOptions { Count = 1 });
        var output = SourcesTestSink.Link(node.Output);

        node.Complete();
        await node.DisposeAsync();

        await node.Completion.WaitAsync(Timeout);
        await output.Completion.WaitAsync(Timeout);
    }

    [Fact]
    public async Task Sequence_emits_lifecycle_events_without_an_error_port()
    {
        await using var node = new SequenceSourceNode(
            new SequenceSourceOptions { Count = 1 });
        var output = SourcesTestSink.Link(node.Output);
        var events = SourcesTestSink.Link(node.Events);

        await node.StartAsync();
        await node.Completion.WaitAsync(Timeout);

        (await SourcesTestSink.DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        var names = (await SourcesTestSink.DrainUntilCompletedAsync(events))
            .Select(flowEvent => flowEvent.Name)
            .ToArray();
        names.ShouldContain(SequenceSourceNode.Started);
        names.ShouldContain(SequenceSourceNode.Emitted);
        names.ShouldContain(SequenceSourceNode.Completed);
        typeof(SequenceSourceNode).GetProperty("Errors").ShouldBeNull();
    }

    [Fact]
    public async Task Sequence_pre_canceled_start_does_not_consume_start_state()
    {
        await using var node = new SequenceSourceNode(
            new SequenceSourceOptions { Count = 1 });
        var output = SourcesTestSink.Link(node.Output);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<TaskCanceledException>(
            () => node.StartAsync(cancellation.Token));
        await node.StartAsync();
        await node.Completion.WaitAsync(Timeout);

        (await SourcesTestSink.DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(0, 0, 1, 1, 1, "boundedCapacity")]
    [InlineData(1, -1, 1, 1, 1, "initialDelayMilliseconds")]
    [InlineData(1, 0, -1, 1, 1, "intervalMilliseconds")]
    [InlineData(1, 0, 1, 0, 1, "count")]
    [InlineData(1, 0, 1, 1, 0, "step")]
    public void Sequence_rejects_invalid_options(
        int boundedCapacity,
        int initialDelay,
        int interval,
        int count,
        long step,
        string expected)
    {
        var exception = Should.Throw<ArgumentException>(() => new SequenceSourceNode(
            new SequenceSourceOptions
            {
                BoundedCapacity = boundedCapacity,
                InitialDelayMilliseconds = initialDelay,
                IntervalMilliseconds = interval,
                Count = count,
                Step = step
            }));

        exception.Message.ShouldContain(expected, Case.Insensitive);
    }

    [Fact]
    public void Sequence_rejects_null_options()
        => Should.Throw<ArgumentNullException>(() => new SequenceSourceNode(null!));

    private static async Task AdvanceUntilCompletedAsync(
        TrackingFakeTimeProvider clock,
        IFlowNode node,
        TimeSpan step)
    {
        var fired = 0;
        while (!node.Completion.IsCompleted)
        {
            var scheduled = clock.TimerScheduled;
            if (clock.CreatedTimerCount > fired)
            {
                clock.Advance(step);
                fired++;
                continue;
            }

            await Task.WhenAny(scheduled, node.Completion).WaitAsync(Timeout);
        }

        await node.Completion.WaitAsync(Timeout);
    }
}
