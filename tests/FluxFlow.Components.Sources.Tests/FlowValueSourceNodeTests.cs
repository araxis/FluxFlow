using FluxFlow.Components.Sources.Nodes;
using FluxFlow.Components.Sources.Options;
using FluxFlow.Data;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Sources.Tests;

public sealed class FlowValueSourceNodeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Generated_emits_immutable_values_with_fresh_lineage_and_events()
    {
        var first = FlowValue.FromObject(new Dictionary<string, FlowValue>
        {
            ["id"] = FlowValue.From("A-100"),
            ["value"] = FlowValue.From(10)
        });
        await using var node = new FlowValueGeneratedSourceNode(
            new FlowValueGeneratedSourceOptions { Name = "orders", BoundedCapacity = 8 },
            [first, FlowValue.From("done")]);
        var output = SourcesTestSink.Link(node.Output);
        var events = SourcesTestSink.Link(node.Events);

        await node.StartAsync();
        await node.Completion.WaitAsync(Timeout);

        var messages = await SourcesTestSink.DrainUntilCompletedAsync(output);
        messages.Select(message => message.Payload).ShouldBe([first, FlowValue.From("done")]);
        messages.Select(message => message.CorrelationId).Distinct().Count().ShouldBe(2);
        messages.ShouldAllBe(message => !message.TraceId.IsEmpty);
        var eventNames = (await SourcesTestSink.DrainUntilCompletedAsync(events))
            .Select(@event => @event.Name)
            .ToArray();
        eventNames.ShouldContain(FlowValueGeneratedSourceNode.Started);
        eventNames.ShouldContain(FlowValueGeneratedSourceNode.Emitted);
        eventNames.ShouldContain(FlowValueGeneratedSourceNode.Completed);
        typeof(FlowValueGeneratedSourceNode).GetProperty("Errors").ShouldBeNull();
    }

    [Fact]
    public async Task Generated_pre_canceled_start_does_not_consume_start_state()
    {
        await using var node = new FlowValueGeneratedSourceNode(
            new FlowValueGeneratedSourceOptions(),
            [FlowValue.From("ready")]);
        var output = SourcesTestSink.Link(node.Output);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<TaskCanceledException>(() => node.StartAsync(cancellation.Token));
        await node.StartAsync();
        await node.Completion.WaitAsync(Timeout);

        var message = (await SourcesTestSink.DrainUntilCompletedAsync(output))
            .ShouldHaveSingleItem();
        message.Payload.GetString().ShouldBe("ready");
    }

    [Fact]
    public async Task Generated_loops_until_max_items()
    {
        await using var node = new FlowValueGeneratedSourceNode(
            new FlowValueGeneratedSourceOptions { Loop = true, MaxItems = 5 },
            [FlowValue.From(1), FlowValue.From(2)]);
        var output = SourcesTestSink.Link(node.Output);

        await node.StartAsync();
        await node.Completion.WaitAsync(Timeout);

        (await SourcesTestSink.DrainUntilCompletedAsync(output))
            .Select(message => (long)message.Payload.GetInteger())
            .ShouldBe([1, 2, 1, 2, 1]);
    }

    [Fact]
    public async Task Sequence_emits_flowvalue_objects_with_fresh_lineage()
    {
        var startedAt = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        var clock = new TrackingFakeTimeProvider(startedAt);
        await using var node = new FlowValueSequenceSourceNode(
            new SequenceSourceOptions
            {
                Name = "numbers",
                Start = 10,
                Step = 5,
                Count = 3
            },
            clock);
        var output = SourcesTestSink.Link(node.Output);

        await node.StartAsync();
        await node.Completion.WaitAsync(Timeout);

        var messages = await SourcesTestSink.DrainUntilCompletedAsync(output);
        messages.Select(message => (long)message.Payload.GetObject()["sequence"].GetInteger())
            .ShouldBe([1, 2, 3]);
        messages.Select(message => (long)message.Payload.GetObject()["value"].GetInteger())
            .ShouldBe([10, 15, 20]);
        messages.ShouldAllBe(message =>
            message.Payload.GetObject()["name"].GetString() == "numbers");
        messages.ShouldAllBe(message =>
            message.Payload.GetObject()["timestamp"].GetDateTimeOffset() == startedAt);
        messages.Select(message => message.CorrelationId).Distinct().Count().ShouldBe(3);
        typeof(FlowValueSequenceSourceNode).GetProperty("Errors").ShouldBeNull();
    }

    [Fact]
    public async Task Sequence_complete_before_start_settles_outputs()
    {
        await using var node = new FlowValueSequenceSourceNode(
            new SequenceSourceOptions { Count = 1 });
        var output = SourcesTestSink.Link(node.Output);

        node.Complete();
        await node.DisposeAsync();

        await node.Completion.WaitAsync(Timeout);
        await output.Completion.WaitAsync(Timeout);
    }

    [Theory]
    [InlineData(0, 0, 1, 1, "bounded")]
    [InlineData(1, -1, 1, 1, "initialDelayMilliseconds")]
    [InlineData(1, 0, -1, 1, "intervalMilliseconds")]
    [InlineData(1, 0, 1, 0, "maxItems")]
    public void Generated_rejects_invalid_options(
        int boundedCapacity,
        int initialDelay,
        int interval,
        int maxItems,
        string expected)
    {
        var exception = Should.Throw<ArgumentException>(() =>
            new FlowValueGeneratedSourceNode(
                new FlowValueGeneratedSourceOptions
                {
                    BoundedCapacity = boundedCapacity,
                    InitialDelayMilliseconds = initialDelay,
                    IntervalMilliseconds = interval,
                    MaxItems = maxItems
                },
                [FlowValue.From("item")]));

        exception.Message.ShouldContain(expected, Case.Insensitive);
    }
}
