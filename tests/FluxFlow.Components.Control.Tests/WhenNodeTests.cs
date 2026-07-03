using FluxFlow.Components.Control.Diagnostics;
using FluxFlow.Components.Control.Nodes;
using FluxFlow.Components.Control.Options;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Control.Tests;

// Every test news the node directly — no engine, no registry. Messages travel as
// FlowMessage<T> envelopes; the correlation id flows input -> WhenTrue/WhenFalse
// and onto any error for free.
public sealed class WhenNodeTests
{
    [Fact]
    public async Task RoutesItemsToTrueAndFalsePreservingCorrelationId()
    {
        await using var node = new WhenNode<int>(
            Options("route", inputType: "int"),
            new RecordingExpressionEngine(
                evaluate: (_, context, _) => ((int)context.Variables["input"]!) >= 10));
        var whenTrue = Sink(node.WhenTrue);
        var whenFalse = Sink(node.WhenFalse);

        var low = FlowMessage.Create(5);
        var high = FlowMessage.Create(12);
        await node.Input.SendAsync(low);
        await node.Input.SendAsync(high);

        var rejected = await whenFalse.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        rejected.Payload.ShouldBe(5);
        rejected.CorrelationId.ShouldBe(low.CorrelationId);

        var accepted = await whenTrue.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        accepted.Payload.ShouldBe(12);
        accepted.CorrelationId.ShouldBe(high.CorrelationId);
    }

    [Fact]
    public async Task WhenTrueIsTheBroadcastOutputPort()
    {
        // WhenTrue is the node's primary Output: linking either gets the same stream.
        await using var node = new WhenNode<int>(
            Options("route", inputType: "int"),
            new RecordingExpressionEngine(evaluate: (_, _, _) => true));
        var viaWhenTrue = Sink(node.WhenTrue);
        var viaOutput = Sink(node.Output);
        node.WhenFalse.LinkTo(DataflowBlock.NullTarget<FlowMessage<int>>());

        await node.Input.SendAsync(FlowMessage.Create(7));

        (await viaWhenTrue.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.ShouldBe(7);
        (await viaOutput.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.ShouldBe(7);
    }

    [Fact]
    public async Task WhenFalse_FansOutToEveryConsumer()
    {
        await using var node = new WhenNode<int>(
            Options("route", inputType: "int"),
            new RecordingExpressionEngine(evaluate: (_, _, _) => false));
        var logger = Sink(node.WhenFalse);
        var mapper = Sink(node.WhenFalse);
        node.WhenTrue.LinkTo(DataflowBlock.NullTarget<FlowMessage<int>>());

        await node.Input.SendAsync(FlowMessage.Create(1));
        await node.Input.SendAsync(FlowMessage.Create(2));

        (await logger.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.ShouldBe(1);
        (await logger.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.ShouldBe(2);
        (await mapper.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.ShouldBe(1);
        (await mapper.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.ShouldBe(2);
    }

    [Fact]
    public async Task ExpressionFailure_ReportsErrorWithCorrelationIdAndContinues()
    {
        var calls = 0;
        await using var node = new WhenNode<string>(
            Options("route", expressionName: "when-test"),
            new RecordingExpressionEngine(
                evaluate: (_, _, _) =>
                {
                    calls++;
                    if (calls == 1)
                    {
                        throw new InvalidOperationException("route failed");
                    }

                    return true;
                }));
        var errors = Sink(node.Errors);
        var whenTrue = Sink(node.WhenTrue);
        node.WhenFalse.LinkTo(DataflowBlock.NullTarget<FlowMessage<string>>());

        var bad = FlowMessage.Create("first");
        await node.Input.SendAsync(bad);
        await node.Input.SendAsync(FlowMessage.Create("second"));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(ControlErrorCodes.WhenExpressionFailed);
        error.CorrelationId.ShouldBe(bad.CorrelationId);
        error.Context!.ShouldContain("expressionName=when-test");

        (await whenTrue.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.ShouldBe("second");
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task EmitsRouteEventCarryingCorrelationId()
    {
        await using var node = new WhenNode<string>(
            Options("route", expressionId: "route-v1"),
            new RecordingExpressionEngine(evaluate: (_, _, _) => false));
        node.WhenTrue.LinkTo(DataflowBlock.NullTarget<FlowMessage<string>>());
        node.WhenFalse.LinkTo(DataflowBlock.NullTarget<FlowMessage<string>>());
        var events = Sink(node.Events);

        var message = FlowMessage.Create("value");
        await node.Input.SendAsync(message);

        var @event = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        @event.Name.ShouldBe(ControlDiagnosticNames.WhenRouted);
        @event.CorrelationId.ShouldBe(message.CorrelationId);
        @event.Attributes["route"].ShouldBe(ControlComponentPorts.WhenFalse);
        @event.Attributes["expressionId"].ShouldBe("route-v1");
    }

    [Fact]
    public async Task ConfiguredClock_StampsErrorTimestamp()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-02T13:00:00Z");
        await using var node = new WhenNode<int>(
            Options("boom", inputType: "int"),
            new RecordingExpressionEngine(evaluate: (_, _, _) => throw new InvalidOperationException("boom")),
            contextFactory: null,
            clock: new FakeTimeProvider(timestamp));
        var errors = Sink(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(1));

        (await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Timestamp.ShouldBe(timestamp);
    }

    [Fact]
    public async Task UsesProvidedContextFactoryVariables()
    {
        await using var node = new WhenNode<Message>(
            Options("matches"),
            new RecordingExpressionEngine(evaluate: (_, context, _) => context.Variables["matches"]),
            new MatchingContextFactory<Message>(matches: true));
        var whenTrue = Sink(node.WhenTrue);
        node.WhenFalse.LinkTo(DataflowBlock.NullTarget<FlowMessage<Message>>());

        var message = new Message("value");
        var sent = FlowMessage.Create(message);
        await node.Input.SendAsync(sent);

        var received = await whenTrue.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        received.Payload.ShouldBe(message);
        received.CorrelationId.ShouldBe(sent.CorrelationId);
    }

    [Fact]
    public async Task Completion_PropagatesToBothBranchSinks()
    {
        var node = new WhenNode<int>(
            Options("route", inputType: "int"),
            new RecordingExpressionEngine(evaluate: (_, _, _) => true));
        var whenTrue = new BufferBlock<FlowMessage<int>>();
        var whenFalse = new BufferBlock<FlowMessage<int>>();
        node.WhenTrue.LinkTo(whenTrue, new DataflowLinkOptions { PropagateCompletion = true });
        node.WhenFalse.LinkTo(whenFalse, new DataflowLinkOptions { PropagateCompletion = true });

        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await whenTrue.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await whenFalse.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Constructor_RequiresExpressionEngine()
        => Should.Throw<ArgumentNullException>(
            () => new WhenNode<int>(Options("route"), expressionEngine: null!));

    [Fact]
    public void Constructor_RequiresPredicate()
        => Should.Throw<ArgumentNullException>(
            () => new WhenNode<int>(Options("route"), predicate: null!));

    [Fact]
    public void Constructor_RequiresNonEmptyExpression()
        => Should.Throw<ArgumentException>(
            () => new WhenNode<int>(
                new ControlExpressionOptions { Expression = "  " },
                new RecordingExpressionEngine()));

    [Fact]
    public void Constructor_WithCompiledPredicate_DoesNotRequireExpression()
        => Should.NotThrow(() => new WhenNode<int>(
            new ControlExpressionOptions(),
            new DelegateFlowPredicate<int>(_ => true)));

    [Fact]
    public void Constructor_RequiresNonEmptyInputType()
        => Should.Throw<ArgumentException>(
            () => new WhenNode<int>(
                new ControlExpressionOptions
                {
                    Expression = "route",
                    InputType = " "
                },
                new RecordingExpressionEngine()));

    [Fact]
    public void Constructor_RequiresPositiveBoundedCapacity()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new WhenNode<int>(
                new ControlExpressionOptions
                {
                    Expression = "route",
                    BoundedCapacity = 0
                },
                new RecordingExpressionEngine()));

    private static ControlExpressionOptions Options(
        string expression,
        string? expressionId = null,
        string? expressionName = null,
        string inputType = "object")
        => new()
        {
            Expression = expression,
            ExpressionId = expressionId,
            ExpressionName = expressionName,
            InputType = inputType
        };

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = false });
        return sink;
    }

    private sealed record Message(string Value);

    private sealed class MatchingContextFactory<TInput>(bool matches) : IFlowMapContextFactory<TInput>
    {
        public FlowMapContext Create(TInput input)
            => new()
            {
                Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["input"] = input,
                    ["value"] = input,
                    ["matches"] = matches
                }
            };
    }
}
