using FluxFlow.Components.Sources.Nodes;
using FluxFlow.Components.Sources.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Sources.Tests;

public sealed class GeneratedSourceNodeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Generated_emits_immutable_values_with_fresh_lineage_and_fan_out()
    {
        var first = FlowValue.FromObject(new Dictionary<string, FlowValue>
        {
            ["id"] = FlowValue.From("A-100"),
            ["value"] = FlowValue.From(10)
        });
        await using var node = new GeneratedSourceNode(
            new GeneratedSourceOptions { Name = "orders", BoundedCapacity = 8 },
            [first, FlowValue.From("done")]);
        var firstOutput = SourcesTestSink.Link(node.Output);
        var secondOutput = SourcesTestSink.Link(node.Output);

        await node.StartAsync();
        await node.Completion.WaitAsync(Timeout);

        var firstMessages = await SourcesTestSink.DrainUntilCompletedAsync(firstOutput);
        var secondMessages = await SourcesTestSink.DrainUntilCompletedAsync(secondOutput);
        firstMessages.Select(message => message.Payload)
            .ShouldBe([first, FlowValue.From("done")]);
        firstMessages.Select(message => message.CorrelationId).Distinct().Count().ShouldBe(2);
        firstMessages.ShouldAllBe(message => !message.TraceId.IsEmpty);
        firstMessages[0].Payload.ShouldBeSameAs(secondMessages[0].Payload);
        firstMessages[1].Payload.ShouldBeSameAs(secondMessages[1].Payload);
    }

    [Fact]
    public async Task Generated_loops_until_max_items()
    {
        await using var node = new GeneratedSourceNode(
            new GeneratedSourceOptions { Loop = true, MaxItems = 5 },
            [FlowValue.From(1), FlowValue.From(2)]);
        var output = SourcesTestSink.Link(node.Output);

        await node.StartAsync();
        await node.Completion.WaitAsync(Timeout);

        (await SourcesTestSink.DrainUntilCompletedAsync(output))
            .Select(message => (long)message.Payload.GetInteger())
            .ShouldBe([1, 2, 1, 2, 1]);
    }

    [Fact]
    public async Task Generated_uses_configured_clock_for_timing()
    {
        var clock = new TrackingFakeTimeProvider(
            new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero));
        await using var node = new GeneratedSourceNode(
            new GeneratedSourceOptions
            {
                InitialDelayMilliseconds = 15,
                IntervalMilliseconds = 30
            },
            [FlowValue.From("one"), FlowValue.From("two")],
            clock);
        var output = SourcesTestSink.Link(node.Output);

        await node.StartAsync();
        await AdvanceUntilCompletedAsync(clock, node, TimeSpan.FromMilliseconds(30));

        (await SourcesTestSink.DrainUntilCompletedAsync(output))
            .Select(message => message.Payload.GetString())
            .ShouldBe(["one", "two"]);
    }

    [Fact]
    public async Task Generated_completes_an_empty_item_list()
    {
        await using var node = new GeneratedSourceNode(
            new GeneratedSourceOptions(),
            []);
        var output = SourcesTestSink.Link(node.Output);

        await node.StartAsync();
        await node.Completion.WaitAsync(Timeout);

        (await SourcesTestSink.DrainUntilCompletedAsync(output)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Generated_emits_lifecycle_events_without_an_error_port()
    {
        await using var node = new GeneratedSourceNode(
            new GeneratedSourceOptions { Name = "demo" },
            [FlowValue.From("one")]);
        var output = SourcesTestSink.Link(node.Output);
        var events = SourcesTestSink.Link(node.Events);

        await node.StartAsync();
        await node.Completion.WaitAsync(Timeout);

        (await SourcesTestSink.DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        var names = (await SourcesTestSink.DrainUntilCompletedAsync(events))
            .Select(flowEvent => flowEvent.Name)
            .ToArray();
        names.ShouldContain(GeneratedSourceNode.Started);
        names.ShouldContain(GeneratedSourceNode.Emitted);
        names.ShouldContain(GeneratedSourceNode.Completed);
        typeof(GeneratedSourceNode).GetProperty("Errors").ShouldBeNull();
    }

    [Fact]
    public async Task Generated_complete_before_start_settles_output()
    {
        await using var node = new GeneratedSourceNode(
            new GeneratedSourceOptions(),
            [FlowValue.From("one")]);
        var output = SourcesTestSink.Link(node.Output);

        node.Complete();
        await node.DisposeAsync();

        await node.Completion.WaitAsync(Timeout);
        await output.Completion.WaitAsync(Timeout);
    }

    [Fact]
    public async Task Generated_pre_canceled_start_does_not_consume_start_state()
    {
        await using var node = new GeneratedSourceNode(
            new GeneratedSourceOptions(),
            [FlowValue.From("ready")]);
        var output = SourcesTestSink.Link(node.Output);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<TaskCanceledException>(
            () => node.StartAsync(cancellation.Token));
        await node.StartAsync();
        await node.Completion.WaitAsync(Timeout);

        var message = (await SourcesTestSink.DrainUntilCompletedAsync(output))
            .ShouldHaveSingleItem();
        message.Payload.GetString().ShouldBe("ready");
    }

    [Fact]
    public async Task Generated_runtime_failure_emits_event_and_faults_completion()
    {
        var failure = new InvalidOperationException("timer failed");
        await using var node = new GeneratedSourceNode(
            new GeneratedSourceOptions { InitialDelayMilliseconds = 1 },
            [FlowValue.From("one")],
            new ThrowingTimeProvider(failure));
        var events = SourcesTestSink.Link(node.Events);

        await node.StartAsync();

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => node.Completion.WaitAsync(Timeout));
        thrown.ShouldBeSameAs(failure);
        (await SourcesTestSink.DrainUntilCompletedAsync(events))
            .ShouldContain(@event =>
                @event.Name == GeneratedSourceNode.Failed &&
                @event.Level == FlowEventLevel.Error);
    }

    [Fact]
    public void Generated_rejects_loop_without_max_items()
        => Should.Throw<ArgumentException>(() => new GeneratedSourceNode(
                new GeneratedSourceOptions { Loop = true },
                [FlowValue.From("one")]))
            .Message.ShouldContain("maxItems");

    [Theory]
    [InlineData(0, 0, 0, 1, "capacity")]
    [InlineData(1, -1, 0, 1, "initialDelayMilliseconds")]
    [InlineData(1, 0, -1, 1, "intervalMilliseconds")]
    [InlineData(1, 0, 0, 0, "maxItems")]
    public void Generated_rejects_invalid_options(
        int boundedCapacity,
        int initialDelay,
        int interval,
        int maxItems,
        string expected)
    {
        var exception = Should.Throw<ArgumentException>(() => new GeneratedSourceNode(
            new GeneratedSourceOptions
            {
                BoundedCapacity = boundedCapacity,
                InitialDelayMilliseconds = initialDelay,
                IntervalMilliseconds = interval,
                MaxItems = maxItems
            },
            [FlowValue.From("one")]));

        exception.Message.ShouldContain(expected, Case.Insensitive);
    }

    [Fact]
    public void Generated_rejects_null_items()
        => Should.Throw<ArgumentNullException>(() => new GeneratedSourceNode(
            new GeneratedSourceOptions(),
            null!));

    [Fact]
    public void Generated_rejects_null_options()
        => Should.Throw<ArgumentNullException>(() => new GeneratedSourceNode(
            null!,
            [FlowValue.From("one")]));

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

    private sealed class ThrowingTimeProvider(Exception failure) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
            => new(2026, 6, 2, 12, 0, 0, TimeSpan.Zero);

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
            => throw failure;
    }
}
