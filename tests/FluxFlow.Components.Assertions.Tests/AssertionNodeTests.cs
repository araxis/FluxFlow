using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Assertions.Contracts;
using FluxFlow.Components.Assertions.Diagnostics;
using FluxFlow.Components.Assertions.Nodes;
using FluxFlow.Components.Assertions.Options;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Assertions.Tests;

public sealed class AssertionNodeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Passed_and_failed_assertions_are_typed_results()
    {
        await using var node = new AssertionNode<ScoreInput>(
            new AssertionOptions
            {
                Expression = "score >= 10",
                Description = "score-check",
                FailureMessage = "Score too low."
            },
            new RecordingExpressionEngine(
                evaluate: (_, context, _) =>
                    ((ScoreInput)context.Variables["input"]!).Score >= 10));
        var output = Sink(node.Output);
        var highValue = new ScoreInput(12);
        var lowValue = new ScoreInput(3);
        var high = FlowMessage.Create(highValue, new CorrelationId("assert-passed"));
        var low = FlowMessage.Create(lowValue, new CorrelationId("assert-failed"));

        await node.Input.SendAsync(high);
        await node.Input.SendAsync(low);

        var passed = await output.ReceiveAsync().WaitAsync(Timeout);
        var failed = await output.ReceiveAsync().WaitAsync(Timeout);
        passed.IsError.ShouldBeFalse();
        passed.Value.Passed.ShouldBeTrue();
        passed.Value.Input.ShouldBeSameAs(highValue);
        passed.Value.Description.ShouldBe("score-check");
        passed.CorrelationId.ShouldBe(high.CorrelationId);
        passed.TraceId.ShouldBe(high.TraceId);
        passed.CausationId.ShouldBe(high.MessageId);
        passed.MessageId.ShouldNotBe(high.MessageId);
        failed.IsError.ShouldBeFalse();
        failed.Value.Passed.ShouldBeFalse();
        failed.Value.Input.ShouldBeSameAs(lowValue);
        failed.Value.Message.ShouldBe("Score too low.");
        failed.CorrelationId.ShouldBe(low.CorrelationId);
    }

    [Fact]
    public async Task Expression_failure_is_an_error_message_and_later_input_continues()
    {
        var calls = 0;
        await using var node = new AssertionNode<ScoreInput>(
            new AssertionOptions
            {
                Expression = "assert",
                ExpressionName = "score-rule"
            },
            new RecordingExpressionEngine(evaluate: (_, _, _) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                    throw new InvalidOperationException("evaluation failed");
                return true;
            }));
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new ScoreInput(1)));
        await node.Input.SendAsync(FlowMessage.Create(new ScoreInput(2)));

        var failure = await output.ReceiveAsync().WaitAsync(Timeout);
        var success = await output.ReceiveAsync().WaitAsync(Timeout);
        failure.IsError.ShouldBeTrue();
        failure.Error.ShouldNotBeNull().Code.ShouldBe(AssertionErrorCodeNames.EvaluationFailed);
        failure.Error.Details!.Value.GetProperty("expressionName").GetString()
            .ShouldBe("score-rule");
        success.IsError.ShouldBeFalse();
        success.Value.Passed.ShouldBeTrue();
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Context_factory_receives_exact_typed_value()
    {
        var contextFactory = new RecordingContextFactory();
        await using var node = new AssertionNode<ScoreInput>(
            new AssertionOptions { Expression = "passed" },
            new RecordingExpressionEngine(
                evaluate: (_, context, _) => context.Variables["passed"]),
            contextFactory);
        var output = Sink(node.Output);
        var input = new ScoreInput(4);

        await node.Input.SendAsync(FlowMessage.Create(input));

        var result = await output.ReceiveAsync().WaitAsync(Timeout);
        result.Value.Passed.ShouldBeTrue();
        contextFactory.Input.ShouldBeSameAs(input);
    }

    [Fact]
    public async Task Events_and_results_use_injected_clock()
    {
        var timestamp = DateTimeOffset.Parse("2026-07-19T12:00:00Z");
        await using var node = new AssertionNode<ScoreInput>(
            new AssertionOptions
            {
                Expression = "assert",
                ExpressionId = "assert-v2"
            },
            new RecordingExpressionEngine(evaluate: (_, _, _) => false),
            clock: new FakeTimeProvider(timestamp));
        var output = Sink(node.Output);
        var events = Sink(node.Events);

        await node.Input.SendAsync(FlowMessage.Create(new ScoreInput(5)));

        var result = await output.ReceiveAsync().WaitAsync(Timeout);
        var @event = await events.ReceiveAsync().WaitAsync(Timeout);
        result.Value.EvaluatedAt.ShouldBe(timestamp);
        @event.Timestamp.ShouldBe(timestamp);
        @event.Name.ShouldBe(AssertionDiagnosticNames.Evaluated);
        @event.Level.ShouldBe(FlowEventLevel.Information);
        @event.Attributes["resultKind"].ShouldBe(AssertionResultKinds.Failed);
        @event.Attributes["passed"].ShouldBe(false);
        @event.Attributes["isError"].ShouldBe(false);
        @event.Attributes["expressionId"].ShouldBe("assert-v2");
    }

    [Fact]
    public async Task Completion_propagates_to_output()
    {
        await using var node = new AssertionNode<ScoreInput>(
            new AssertionOptions { Expression = "assert" },
            new RecordingExpressionEngine(evaluate: (_, _, _) => true));
        var output = new BufferBlock<FlowMessage<AssertionResult<ScoreInput>>>();
        node.Output.LinkTo(output, new DataflowLinkOptions { PropagateCompletion = true });

        node.Complete();

        await node.Completion.WaitAsync(Timeout);
        await output.Completion.WaitAsync(Timeout);
    }

    [Fact]
    public async Task Output_fans_out_accepted_results_in_order()
    {
        await using var node = new AssertionNode<int>(
            new AssertionOptions { Expression = "assert" },
            new RecordingExpressionEngine(
                evaluate: (_, context, _) => (int)context.Variables["input"]! > 0));
        var first = Sink(node.Output);
        var second = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(1));
        await node.Input.SendAsync(FlowMessage.Create(-1));

        var firstResults = new[]
        {
            (await first.ReceiveAsync().WaitAsync(Timeout)).Value.Passed,
            (await first.ReceiveAsync().WaitAsync(Timeout)).Value.Passed
        };
        var secondResults = new[]
        {
            (await second.ReceiveAsync().WaitAsync(Timeout)).Value.Passed,
            (await second.ReceiveAsync().WaitAsync(Timeout)).Value.Passed
        };
        firstResults.ShouldBe([true, false]);
        secondResults.ShouldBe(firstResults);
    }

    [Fact]
    public void Json_facade_rejects_invalid_options_and_has_no_legacy_ports()
    {
        var engine = new RecordingExpressionEngine(evaluate: (_, _, _) => true);
        Should.Throw<ArgumentException>(() =>
            new JsonAssertionNode(new AssertionOptions(), engine))
            .Message.ShouldContain("expression");
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new JsonAssertionNode(
                new AssertionOptions { Expression = "assert", BoundedCapacity = 0 },
                engine))
            .Message.ShouldContain("positive");
        typeof(JsonAssertionNode).GetProperty("Passed").ShouldBeNull();
        typeof(JsonAssertionNode).GetProperty("Failed").ShouldBeNull();
        typeof(JsonAssertionNode).GetProperty("Errors").ShouldBeNull();
        typeof(AssertionOptions).GetProperty("Engine").ShouldBeNull();
    }

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink);
        return sink;
    }

    private sealed class RecordingContextFactory : IFlowMapContextFactory<ScoreInput>
    {
        public ScoreInput? Input { get; private set; }

        public FlowMapContext Create(ScoreInput input)
        {
            Input = input;
            return new FlowMapContext
            {
                Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["input"] = input,
                    ["value"] = input,
                    ["passed"] = true
                }
            };
        }
    }

    private sealed record ScoreInput(int Score);
}
