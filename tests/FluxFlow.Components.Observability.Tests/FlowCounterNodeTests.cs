using FluxFlow.Components.Observability.Nodes;
using FluxFlow.Components.Observability.Options;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Observability.Tests;

// Every test news the node directly — no engine registry, no runtime. The counter
// takes an IFlowExpressionEngine (and optional IFlowMapContextFactory) straight from
// FluxFlow.Mapping; messages travel as FlowMessage<T> envelopes and the correlation
// id flows input -> snapshot and onto any error for free.
public sealed class FlowCounterNodeTests
{
    [Fact]
    public async Task Counter_CountsMatchingInputsPreservingCorrelationId()
    {
        await using var node = new FlowCounterNode<InputMessage>(
            new FlowCounterOptions
            {
                InputType = "message",
                Name = "accepted",
                Predicate = "enabled"
            },
            new RecordingExpressionEngine(
                (_, context, _) => ((InputMessage)context.Variables["input"]!).Enabled));
        var snapshots = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new InputMessage("first", [1], false)));
        var second = FlowMessage.Create(new InputMessage("second", [1], true));
        await node.Input.SendAsync(second);
        await node.Input.SendAsync(FlowMessage.Create(new InputMessage("third", [1], true)));

        var firstSnapshot = await snapshots.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var secondSnapshot = await snapshots.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        firstSnapshot.CorrelationId.ShouldBe(second.CorrelationId);   // first ACCEPTED input
        firstSnapshot.Payload.Count.ShouldBe(1);
        firstSnapshot.Payload.RejectedCount.ShouldBe(1);
        secondSnapshot.Payload.Count.ShouldBe(2);
        secondSnapshot.Payload.Name.ShouldBe("accepted");
        secondSnapshot.Payload.InputType.ShouldBe("message");
        snapshots.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Output_FansOutEverySnapshotToEveryConsumer()
    {
        await using var node = new FlowCounterNode<string>(
            new FlowCounterOptions { InputType = "string", Name = "items" });
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
    public async Task Counter_ReportsPredicateFailureAndContinues()
    {
        var calls = 0;
        await using var node = new FlowCounterNode<int>(
            new FlowCounterOptions
            {
                InputType = "int",
                Predicate = "ok",
                ExpressionName = "counter-test"
            },
            new RecordingExpressionEngine((_, _, _) =>
            {
                calls++;
                if (calls == 1)
                {
                    throw new InvalidOperationException("predicate failed");
                }

                return true;
            }));
        var snapshots = Sink(node.Output);
        var errors = Sink(node.Errors);

        var bad = FlowMessage.Create(1);
        await node.Input.SendAsync(bad);
        await node.Input.SendAsync(FlowMessage.Create(2));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(ObservabilityErrorCodes.CounterPredicateFailed);
        error.CorrelationId.ShouldBe(bad.CorrelationId);
        error.Context!.ShouldContain("expressionName=counter-test");

        var snapshot = await snapshots.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        snapshot.Payload.Count.ShouldBe(1);
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Counter_WithoutPredicateDoesNotRequireExpressionEngine()
    {
        var timestamp = new DateTimeOffset(2026, 6, 2, 18, 31, 0, TimeSpan.Zero);
        await using var node = new FlowCounterNode<string>(
            new FlowCounterOptions { InputType = "string" },
            clock: new FakeTimeProvider(timestamp));
        var snapshots = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create("one"));

        var snapshot = await snapshots.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        snapshot.Payload.Count.ShouldBe(1);
        snapshot.Payload.Timestamp.ShouldBe(timestamp);
        snapshot.Payload.LastObservedAt.ShouldBe(timestamp);
    }

    [Fact]
    public async Task Counter_UsesSuppliedContextFactory()
    {
        await using var node = new FlowCounterNode<DerivedCounterMessage>(
            new FlowCounterOptions
            {
                InputType = "derived-message",
                Predicate = "accepted"
            },
            new RecordingExpressionEngine((_, context, _) => context.Variables["accepted"]),
            new TestContextFactory<DerivedCounterMessage>(accepted: true));
        var snapshots = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new DerivedCounterMessage("first")));

        var snapshot = await snapshots.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        snapshot.Payload.Count.ShouldBe(1);
        snapshot.Payload.RejectedCount.ShouldBe(0);
    }

    [Fact]
    public async Task Counter_EmitsEventCarryingCorrelationId()
    {
        await using var node = new FlowCounterNode<string>(
            new FlowCounterOptions { InputType = "string", Name = "items" });
        Sink(node.Output);
        var events = Sink(node.Events);

        var sent = FlowMessage.Create("hello");
        await node.Input.SendAsync(sent);

        var @event = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        @event.Name.ShouldBe(FlowCounterNode<string>.Incremented);
        @event.CorrelationId.ShouldBe(sent.CorrelationId);
        @event.Attributes["name"].ShouldBe("items");
    }

    [Fact]
    public void Counter_RequiresExpressionEngineWhenPredicateConfigured()
        => Should.Throw<ArgumentNullException>(
            () => new FlowCounterNode<string>(
                new FlowCounterOptions { InputType = "string", Predicate = "ok" }));

    [Fact]
    public void Constructor_RequiresOptions()
        => Should.Throw<ArgumentNullException>(() => new FlowCounterNode<string>(null!));

    [Fact]
    public void Constructor_RequiresNonEmptyInputType()
        => Should.Throw<ArgumentException>(
            () => new FlowCounterNode<string>(
                new FlowCounterOptions { InputType = " " }));

    [Fact]
    public void Constructor_RequiresPositiveBoundedCapacity()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new FlowCounterNode<string>(
                new FlowCounterOptions { BoundedCapacity = 0 }));

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = false });
        return sink;
    }

    private sealed record InputMessage(string Kind, byte[] Payload, bool Enabled);

    private sealed record DerivedCounterMessage(string Name);

    private sealed class TestContextFactory<TInput>(bool accepted) : IFlowMapContextFactory<TInput>
    {
        public FlowMapContext Create(TInput input)
            => new()
            {
                Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["input"] = input,
                    ["accepted"] = accepted
                }
            };
    }
}
