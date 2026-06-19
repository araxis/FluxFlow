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
// FlowMessage<T> envelopes; the correlation id flows input -> output and onto any
// error for free.
public sealed class FilterNodeTests
{
    [Fact]
    public async Task EmitsOnlyMatchingItemsAndPreservesCorrelationId()
    {
        await using var node = new FilterNode<int>(
            Options("is-even", inputType: "int"),
            new RecordingExpressionEngine(
                evaluate: (_, context, _) => ((int)context.Variables["input"]!) % 2 == 0));
        var output = Sink(node.Output);

        var one = FlowMessage.Create(1);
        var two = FlowMessage.Create(2);
        var three = FlowMessage.Create(3);
        await node.Input.SendAsync(one);
        await node.Input.SendAsync(two);
        await node.Input.SendAsync(three);

        var received = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        received.Payload.ShouldBe(2);
        received.CorrelationId.ShouldBe(two.CorrelationId);   // the whole point of the envelope
        output.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Output_FansOutEveryMatchToEveryConsumer()
    {
        // One node's output linked to two downstream consumers, no engine. Both
        // see every surviving message.
        await using var node = new FilterNode<int>(
            Options("pass"),
            new RecordingExpressionEngine(evaluate: (_, _, _) => true));
        var logger = Sink(node.Output);
        var mapper = Sink(node.Output);

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
        await using var node = new FilterNode<int>(
            Options("test", expressionName: "filter-test", inputType: "int"),
            new RecordingExpressionEngine(
                evaluate: (_, context, _) =>
                {
                    calls++;
                    if (calls == 1)
                    {
                        throw new InvalidOperationException("predicate failed");
                    }

                    return (int)context.Variables["input"]! > 1;
                }));
        var errors = Sink(node.Errors);
        var output = Sink(node.Output);

        var bad = FlowMessage.Create(1);
        await node.Input.SendAsync(bad);
        await node.Input.SendAsync(FlowMessage.Create(2));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(ControlErrorCodes.FilterExpressionFailed);
        error.CorrelationId.ShouldBe(bad.CorrelationId);
        error.Context!.ShouldContain("expressionName=filter-test");

        // The pump keeps going: the second message still flows through.
        (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.ShouldBe(2);
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task EmitsPassedEventCarryingCorrelationId()
    {
        await using var node = new FilterNode<string>(
            Options("pass", expressionId: "filter-v1", inputType: "string"),
            new RecordingExpressionEngine(evaluate: (_, _, _) => true));
        Sink(node.Output);
        var events = Sink(node.Events);

        var message = FlowMessage.Create("hello");
        await node.Input.SendAsync(message);

        var @event = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        @event.Name.ShouldBe(ControlDiagnosticNames.FilterPassed);
        @event.Level.ShouldBe(FlowEventLevel.Information);
        @event.CorrelationId.ShouldBe(message.CorrelationId);
        @event.Attributes["inputType"].ShouldBe("string");
        @event.Attributes["expressionId"].ShouldBe("filter-v1");
    }

    [Fact]
    public async Task RejectedItem_EmitsRejectedEventAndDoesNotReachOutput()
    {
        await using var node = new FilterNode<int>(
            Options("reject"),
            new RecordingExpressionEngine(evaluate: (_, _, _) => false));
        var output = Sink(node.Output);
        var events = Sink(node.Events);

        await node.Input.SendAsync(FlowMessage.Create(1));

        (await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Name
            .ShouldBe(ControlDiagnosticNames.FilterRejected);
        output.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task UsesProvidedContextFactoryVariables()
    {
        await using var node = new FilterNode<Message>(
            Options("matches"),
            new RecordingExpressionEngine(evaluate: (_, context, _) => context.Variables["matches"]),
            new MatchingContextFactory<Message>(matches: true));
        var output = Sink(node.Output);

        var message = new Message("value");
        var sent = FlowMessage.Create(message);
        await node.Input.SendAsync(sent);

        var received = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        received.Payload.ShouldBe(message);
        received.CorrelationId.ShouldBe(sent.CorrelationId);
    }

    [Fact]
    public async Task ConfiguredClock_StampsErrorAndEventTimestamps()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-02T13:00:00Z");
        await using var node = new FilterNode<int>(
            Options("boom"),
            new RecordingExpressionEngine(evaluate: (_, _, _) => throw new InvalidOperationException("boom")),
            contextFactory: null,
            clock: new FakeTimeProvider(timestamp));
        var errors = Sink(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(1));

        (await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Timestamp.ShouldBe(timestamp);
    }

    [Fact]
    public async Task Completion_PropagatesToOutputSink()
    {
        var node = new FilterNode<int>(
            Options("pass"),
            new RecordingExpressionEngine(evaluate: (_, _, _) => true));
        var output = new BufferBlock<FlowMessage<int>>();
        node.Output.LinkTo(output, new DataflowLinkOptions { PropagateCompletion = true });

        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await output.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Constructor_RequiresExpressionEngine()
        => Should.Throw<ArgumentNullException>(
            () => new FilterNode<int>(Options("pass"), expressionEngine: null!));

    [Fact]
    public void Constructor_RequiresPredicate()
        => Should.Throw<ArgumentNullException>(
            () => new FilterNode<int>(Options("pass"), predicate: null!));

    [Fact]
    public void Constructor_RequiresNonEmptyExpression()
        => Should.Throw<ArgumentException>(
            () => new FilterNode<int>(
                new ControlExpressionOptions { Expression = "  " },
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
