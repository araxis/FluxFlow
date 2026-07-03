using FluxFlow.Components.Assertions;
using FluxFlow.Components.Assertions.Contracts;
using FluxFlow.Components.Assertions.Diagnostics;
using FluxFlow.Components.Assertions.Nodes;
using FluxFlow.Components.Assertions.Options;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Assertions.Tests;

// Every test news the node directly — no engine, no registry. Messages travel as
// FlowMessage<T> envelopes; the correlation id flows input -> result/Passed/Failed
// and onto any error for free.
public sealed class FlowAssertionComponentTests
{
    [Fact]
    public async Task EmitsResultsAndRoutesInputsPreservingCorrelationId()
    {
        await using var node = new FlowAssertionComponent<InputMessage>(
            new AssertionOptions
            {
                Expression = "score >= 10",
                InputType = "app.input",
                Description = "score-check",
                FailureMessage = "Score too low."
            },
            new RecordingExpressionEngine(
                evaluate: (_, context, _) => (int)context.Variables["score"]! >= 10),
            new InputMessageContextFactory());
        var results = Sink(node.Output);
        var passed = Sink(node.Passed);
        var failed = Sink(node.Failed);

        var high = FlowMessage.Create(new InputMessage(12));
        var low = FlowMessage.Create(new InputMessage(3));
        await node.Input.SendAsync(high);
        await node.Input.SendAsync(low);

        var first = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        first.CorrelationId.ShouldBe(high.CorrelationId);
        first.Payload.Passed.ShouldBeTrue();
        first.Payload.Description.ShouldBe("score-check");
        first.Payload.Message.ShouldBe("Assertion passed.");
        first.Payload.Failure.ShouldBeNull();

        var second = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        second.CorrelationId.ShouldBe(low.CorrelationId);
        second.Payload.Passed.ShouldBeFalse();
        second.Payload.Message.ShouldBe("Score too low.");
        second.Payload.Failure.ShouldNotBeNull();

        var routedPassed = await passed.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        routedPassed.CorrelationId.ShouldBe(high.CorrelationId);
        routedPassed.Payload.Score.ShouldBe(12);

        var routedFailed = await failed.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        routedFailed.CorrelationId.ShouldBe(low.CorrelationId);
        routedFailed.Payload.Score.ShouldBe(3);
    }

    [Fact]
    public async Task Output_FansOutEveryResultToEveryConsumer()
    {
        // One node's output linked to two downstream consumers, no engine. Both see
        // every result.
        await using var node = new FlowAssertionComponent<object>(
            new AssertionOptions { Expression = "assert" },
            new RecordingExpressionEngine(evaluate: (_, _, _) => true));
        var logger = Sink(node.Output);
        var mapper = Sink(node.Output);
        node.Passed.LinkTo(DataflowBlock.NullTarget<FlowMessage<object>>());
        node.Failed.LinkTo(DataflowBlock.NullTarget<FlowMessage<object>>());

        await node.Input.SendAsync(FlowMessage.Create<object>("a"));
        await node.Input.SendAsync(FlowMessage.Create<object>("b"));

        (await logger.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.Value.ShouldBe("a");
        (await logger.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.Value.ShouldBe("b");
        (await mapper.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.Value.ShouldBe("a");
        (await mapper.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.Value.ShouldBe("b");
    }

    [Fact]
    public async Task ReportsExpressionFailureWithCorrelationIdAndContinues()
    {
        var calls = 0;
        await using var node = new FlowAssertionComponent<object>(
            new AssertionOptions { Expression = "assert", ExpressionName = "assert-test" },
            new RecordingExpressionEngine(evaluate: (_, _, _) =>
            {
                calls++;
                if (calls == 1)
                {
                    throw new InvalidOperationException("assert failed");
                }

                return true;
            }));
        var errors = Sink(node.Errors);
        var results = Sink(node.Output);
        node.Passed.LinkTo(DataflowBlock.NullTarget<FlowMessage<object>>());
        node.Failed.LinkTo(DataflowBlock.NullTarget<FlowMessage<object>>());

        var bad = FlowMessage.Create<object>("first");
        await node.Input.SendAsync(bad);
        await node.Input.SendAsync(FlowMessage.Create<object>("second"));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(AssertionErrorCodes.ExpressionFailed);
        error.CorrelationId.ShouldBe(bad.CorrelationId);
        error.Context!.ShouldContain("expressionName=assert-test");

        // The pump keeps going: the second message still evaluates.
        (await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.Passed.ShouldBeTrue();
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task CanSuppressRoutedInputs()
    {
        await using var node = new FlowAssertionComponent<object>(
            new AssertionOptions
            {
                Expression = "assert",
                EmitPassedInput = false,
                EmitFailedInput = false
            },
            new RecordingExpressionEngine(evaluate: (_, _, _) => true));
        var results = Sink(node.Output);
        var passed = Sink(node.Passed);
        var failed = Sink(node.Failed);

        await node.Input.SendAsync(FlowMessage.Create<object>("value"));

        (await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.Passed.ShouldBeTrue();

        // Give any (suppressed) routed posts a chance to land before asserting absence.
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        passed.TryReceive(out _).ShouldBeFalse();
        failed.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task EmitsEvaluatedEventCarryingCorrelationId()
    {
        await using var node = new FlowAssertionComponent<object>(
            new AssertionOptions { Expression = "assert", ExpressionId = "assert-v1" },
            new RecordingExpressionEngine(evaluate: (_, _, _) => true));
        var events = Sink(node.Events);
        node.Output.LinkTo(DataflowBlock.NullTarget<FlowMessage<FlowAssertionResult>>());
        node.Passed.LinkTo(DataflowBlock.NullTarget<FlowMessage<object>>());
        node.Failed.LinkTo(DataflowBlock.NullTarget<FlowMessage<object>>());

        var message = FlowMessage.Create<object>("value");
        await node.Input.SendAsync(message);

        var @event = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        @event.Name.ShouldBe(AssertionDiagnosticNames.Evaluated);
        @event.Level.ShouldBe(FlowEventLevel.Information);
        @event.CorrelationId.ShouldBe(message.CorrelationId);
        @event.Attributes["passed"].ShouldBe(true);
        @event.Attributes["expressionId"].ShouldBe("assert-v1");
    }

    [Fact]
    public async Task UsesSuppliedContextFactoryForVariables()
    {
        await using var node = new FlowAssertionComponent<DerivedMessage>(
            new AssertionOptions { Expression = "passed", InputType = "message" },
            new RecordingExpressionEngine(evaluate: (_, context, _) => context.Variables["passed"]),
            new TestContextFactory<DerivedMessage>(passed: true));
        var results = Sink(node.Output);
        node.Passed.LinkTo(DataflowBlock.NullTarget<FlowMessage<DerivedMessage>>());
        node.Failed.LinkTo(DataflowBlock.NullTarget<FlowMessage<DerivedMessage>>());

        await node.Input.SendAsync(FlowMessage.Create(new DerivedMessage("value")));

        (await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.Passed.ShouldBeTrue();
    }

    [Fact]
    public async Task UsesConfiguredClockForEvaluatedAt()
    {
        var evaluatedAt = new DateTimeOffset(2026, 6, 2, 9, 30, 0, TimeSpan.Zero);
        await using var node = new FlowAssertionComponent<object>(
            new AssertionOptions { Expression = "assert" },
            new RecordingExpressionEngine(evaluate: (_, _, _) => true),
            clock: new FakeTimeProvider(evaluatedAt));
        var results = Sink(node.Output);
        node.Passed.LinkTo(DataflowBlock.NullTarget<FlowMessage<object>>());
        node.Failed.LinkTo(DataflowBlock.NullTarget<FlowMessage<object>>());

        await node.Input.SendAsync(FlowMessage.Create<object>("value"));

        (await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5)))
            .Payload.EvaluatedAt.ShouldBe(evaluatedAt);
    }

    [Fact]
    public async Task Completion_PropagatesToOutputSinks()
    {
        var node = new FlowAssertionComponent<object>(
            new AssertionOptions { Expression = "assert" },
            new RecordingExpressionEngine(evaluate: (_, _, _) => true));
        // Propagate completion here so the sink observes the broadcast finishing.
        var results = new BufferBlock<FlowMessage<FlowAssertionResult>>();
        node.Output.LinkTo(results, new DataflowLinkOptions { PropagateCompletion = true });
        node.Passed.LinkTo(DataflowBlock.NullTarget<FlowMessage<object>>());
        node.Failed.LinkTo(DataflowBlock.NullTarget<FlowMessage<object>>());

        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        await results.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Constructor_RejectsMissingExpression()
    {
        var exception = Should.Throw<ArgumentException>(
            () => new FlowAssertionComponent<object>(
                new AssertionOptions(),
                new RecordingExpressionEngine()));

        exception.Message.ShouldContain("expression");
    }

    [Fact]
    public void Constructor_RejectsEmptyInputType()
    {
        var exception = Should.Throw<ArgumentException>(
            () => new FlowAssertionComponent<object>(
                new AssertionOptions { Expression = "assert", InputType = " " },
                new RecordingExpressionEngine()));

        exception.Message.ShouldContain("inputType");
    }

    [Fact]
    public void Constructor_RejectsInvalidBoundedCapacity()
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => new FlowAssertionComponent<object>(
                new AssertionOptions { Expression = "assert", BoundedCapacity = 0 },
                new RecordingExpressionEngine()));

        exception.Message.ShouldContain("boundedCapacity");
    }

    [Fact]
    public void Constructor_RequiresExpressionEngine()
        => Should.Throw<ArgumentNullException>(
            () => new FlowAssertionComponent<object>(
                new AssertionOptions { Expression = "assert" },
                null!));

    private static BufferBlock<FlowMessage<T>> Sink<T>(ISourceBlock<FlowMessage<T>> source)
    {
        var sink = new BufferBlock<FlowMessage<T>>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = false });
        return sink;
    }

    private static BufferBlock<FlowError> Sink(ISourceBlock<FlowError> source)
    {
        var sink = new BufferBlock<FlowError>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = false });
        return sink;
    }

    private static BufferBlock<FlowEvent> Sink(ISourceBlock<FlowEvent> source)
    {
        var sink = new BufferBlock<FlowEvent>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = false });
        return sink;
    }

    private sealed record InputMessage(int Score);

    private sealed record DerivedMessage(string Value);

    private sealed class InputMessageContextFactory : IFlowMapContextFactory<InputMessage>
    {
        public FlowMapContext Create(InputMessage input)
            => new()
            {
                Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["input"] = input,
                    ["value"] = input,
                    ["score"] = input.Score
                }
            };
    }

    private sealed class TestContextFactory<TInput>(bool passed) : IFlowMapContextFactory<TInput>
    {
        public FlowMapContext Create(TInput input)
            => new()
            {
                Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["input"] = input,
                    ["value"] = input,
                    ["passed"] = passed
                }
            };
    }
}
