using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Components.Observability.Nodes;
using FluxFlow.Components.Observability.Options;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Observability.Tests;

// Every test news the node directly — no engine, no registry. Messages travel as
// FlowMessage<T> envelopes; the correlation id flows input -> snapshot and onto any
// error for free.
public sealed class FlowMetricsNodeTests
{
    [Fact]
    public async Task Metrics_TracksCountRateAndSizePreservingCorrelationId()
    {
        var firstObservedAt = new DateTimeOffset(2026, 6, 2, 18, 32, 0, TimeSpan.Zero);
        var secondObservedAt = firstObservedAt.AddSeconds(2);
        // FakeTimeProvider returns the same instant until advanced, so the two
        // observations are spaced by draining the first snapshot before advancing
        // the clock and sending the second input.
        var timeProvider = new FakeTimeProvider(firstObservedAt);
        await using var node = new FlowMetricsNode<InputMessage>(
            new FlowMetricsOptions
            {
                InputType = "message",
                Name = "messages",
                SizeSelector = "payloadBytes"
            },
            new DelegateSelector<InputMessage>((message, _) => message.Payload.Length),
            timeProvider);
        var snapshots = Sink(node.Output);

        var firstMessage = FlowMessage.Create(new InputMessage("first", [1, 2], true));
        await node.Input.SendAsync(firstMessage);
        var first = await snapshots.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        timeProvider.SetUtcNow(secondObservedAt);
        await node.Input.SendAsync(FlowMessage.Create(new InputMessage("second", [1, 2, 3, 4], true)));
        var second = await snapshots.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));

        first.CorrelationId.ShouldBe(firstMessage.CorrelationId);
        first.Payload.Count.ShouldBe(1);
        first.Payload.Timestamp.ShouldBe(firstObservedAt);
        first.Payload.LastObservedAt.ShouldBe(firstObservedAt);
        first.Payload.LastSize.ShouldBe(2);
        first.Payload.TotalSize.ShouldBe(2);
        second.Payload.Count.ShouldBe(2);
        second.Payload.Timestamp.ShouldBe(secondObservedAt);
        second.Payload.LastObservedAt.ShouldBe(secondObservedAt);
        second.Payload.LastSize.ShouldBe(4);
        second.Payload.TotalSize.ShouldBe(6);
        second.Payload.AverageSize.ShouldBe(3);
        second.Payload.CurrentRatePerSecond.ShouldBe(0.5d);
        second.Payload.AverageRatePerSecond.ShouldBe(1d);
    }

    [Fact]
    public async Task Output_FansOutEverySnapshotToEveryConsumer()
    {
        await using var node = new FlowMetricsNode<string>(
            new FlowMetricsOptions { InputType = "string", Name = "items" });
        var logger = Sink(node.Output);
        var mapper = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create("one"));
        await node.Input.SendAsync(FlowMessage.Create("two"));

        (await logger.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.Count.ShouldBe(1);
        (await logger.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.Count.ShouldBe(2);
        (await mapper.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.Count.ShouldBe(1);
        (await mapper.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Metrics_SizeSelectorFailureReportsErrorAndContinues()
    {
        var calls = 0;
        await using var node = new FlowMetricsNode<InputMessage>(
            new FlowMetricsOptions { InputType = "message", SizeSelector = "payloadBytes" },
            new DelegateSelector<InputMessage>((message, _) =>
            {
                calls++;
                if (calls == 1)
                {
                    throw new InvalidOperationException("size failed");
                }

                return message.Payload.Length;
            }));
        var snapshots = Sink(node.Output);
        var errors = Sink(node.Errors);

        var bad = FlowMessage.Create(new InputMessage("first", [1, 2], true));
        await node.Input.SendAsync(bad);
        await node.Input.SendAsync(FlowMessage.Create(new InputMessage("second", [1, 2, 3], true)));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(ObservabilityErrorCodes.MetricsSizeSelectorFailed);
        error.CorrelationId.ShouldBe(bad.CorrelationId);

        var first = await snapshots.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var second = await snapshots.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        first.Payload.Count.ShouldBe(1);
        first.Payload.LastSize.ShouldBeNull();
        second.Payload.Count.ShouldBe(2);
        second.Payload.LastSize.ShouldBe(3);
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Metrics_AveragesSizeOverSizedObservationsOnly()
    {
        await using var node = new FlowMetricsNode<InputMessage>(
            new FlowMetricsOptions { InputType = "message", SizeSelector = "payloadBytes" },
            new DelegateSelector<InputMessage>((message, _) =>
                message.Enabled ? message.Payload.Length : (object?)null));
        var snapshots = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new InputMessage("first", [1, 2], false)));
        await node.Input.SendAsync(FlowMessage.Create(new InputMessage("second", [1, 2, 3, 4], true)));

        var first = await snapshots.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var second = await snapshots.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        first.Payload.AverageSize.ShouldBeNull();
        second.Payload.Count.ShouldBe(2);
        second.Payload.TotalSize.ShouldBe(4);
        second.Payload.AverageSize.ShouldBe(4);
    }

    [Fact]
    public async Task Metrics_EmitsEventCarryingCorrelationId()
    {
        await using var node = new FlowMetricsNode<string>(
            new FlowMetricsOptions { InputType = "string", Name = "items" });
        Sink(node.Output);
        var events = Sink(node.Events);

        var sent = FlowMessage.Create("hello");
        await node.Input.SendAsync(sent);

        var @event = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        @event.Name.ShouldBe(FlowMetricsNode<string>.Observed);
        @event.CorrelationId.ShouldBe(sent.CorrelationId);
        @event.Attributes["name"].ShouldBe("items");
    }

    [Fact]
    public void Constructor_RequiresOptions()
        => Should.Throw<ArgumentNullException>(() => new FlowMetricsNode<string>(null!));

    [Fact]
    public void Constructor_RequiresNonEmptyInputType()
        => Should.Throw<ArgumentException>(
            () => new FlowMetricsNode<string>(
                new FlowMetricsOptions { InputType = " " }));

    [Fact]
    public void Constructor_RequiresPositiveBoundedCapacity()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new FlowMetricsNode<string>(
                new FlowMetricsOptions { BoundedCapacity = 0 }));

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = false });
        return sink;
    }

    private sealed class DelegateSelector<TInput>(
        Func<TInput, ObservabilityNodeContext, object?> selector)
        : IObservabilityValueSelector<TInput>
    {
        public object? Select(TInput input, ObservabilityNodeContext context)
            => selector(input, context);
    }

    private sealed record InputMessage(string Kind, byte[] Payload, bool Enabled);
}
