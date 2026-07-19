using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Assertions.Contracts;
using FluxFlow.Components.Assertions.Diagnostics;
using FluxFlow.Components.Assertions.Nodes;
using FluxFlow.Components.Assertions.Options;
using FluxFlow.Data;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Assertions.Tests;

public sealed class FlowValueAssertionNodeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Passed_and_failed_assertions_are_normal_result_variants()
    {
        await using var node = new FlowValueAssertionNode(
            new FlowValueAssertionOptions
            {
                Expression = "score >= 10",
                Description = "score-check",
                FailureMessage = "Score too low."
            },
            new RecordingExpressionEngine(
                evaluate: (_, context, _) =>
                    ((FlowValue)context.Variables["input"]!)
                        .GetObject()["score"]
                        .GetInteger() >= 10));
        var output = Sink(node.Output);
        var highValue = Score(12);
        var lowValue = Score(3);
        var high = FlowMessage.Create(
            highValue,
            new CorrelationId("assert-passed"));
        var low = FlowMessage.Create(
            lowValue,
            new CorrelationId("assert-failed"));

        await node.Input.SendAsync(high);
        await node.Input.SendAsync(low);

        var passed = await output.ReceiveAsync().WaitAsync(Timeout);
        var failed = await output.ReceiveAsync().WaitAsync(Timeout);
        passed.Payload.Kind.ShouldBe(AssertionResultKinds.Passed);
        passed.Payload.IsError.ShouldBeFalse();
        passed.Payload.Value.ShouldNotBeNull().Passed.ShouldBeTrue();
        passed.Payload.Value.Input.ShouldBeSameAs(highValue);
        passed.Payload.Value.Description.ShouldBe("score-check");
        passed.CorrelationId.ShouldBe(high.CorrelationId);
        passed.TraceId.ShouldBe(high.TraceId);
        passed.CausationId.ShouldBe(high.MessageId);
        passed.MessageId.ShouldNotBe(high.MessageId);
        failed.Payload.Kind.ShouldBe(AssertionResultKinds.Failed);
        failed.Payload.IsError.ShouldBeFalse();
        failed.Payload.Value.ShouldNotBeNull().Passed.ShouldBeFalse();
        failed.Payload.Value.Input.ShouldBeSameAs(lowValue);
        failed.Payload.Value.Message.ShouldBe("Score too low.");
        failed.CorrelationId.ShouldBe(low.CorrelationId);
    }

    [Fact]
    public async Task Expression_failure_is_normal_and_later_input_continues()
    {
        var calls = 0;
        await using var node = new FlowValueAssertionNode(
            new FlowValueAssertionOptions
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
        var firstValue = Score(1);

        await node.Input.SendAsync(FlowMessage.Create(firstValue));
        await node.Input.SendAsync(FlowMessage.Create(Score(2)));

        var failure = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        var success = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        failure.Kind.ShouldBe(AssertionResultKinds.EvaluationFailed);
        failure.Error.ShouldNotBeNull().Code
            .ShouldBe(AssertionErrorCodeNames.EvaluationFailed);
        failure.Error.Details.GetObject()["input"].ShouldBe(firstValue);
        failure.Error.Details.GetObject()["expressionName"].GetString()
            .ShouldBe("score-rule");
        success.Kind.ShouldBe(AssertionResultKinds.Passed);
        success.IsError.ShouldBeFalse();
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Null_input_is_a_normal_error_result()
    {
        await using var node = new FlowValueAssertionNode(
            new FlowValueAssertionOptions { Expression = "assert" },
            new RecordingExpressionEngine(evaluate: (_, _, _) => true));
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create<FlowValue>(null!));

        var result = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        result.Kind.ShouldBe(AssertionResultKinds.MissingInput);
        result.Error.ShouldNotBeNull().Code.ShouldBe(AssertionErrorCodeNames.MissingInput);
    }

    [Fact]
    public async Task Context_factory_receives_exact_flow_value()
    {
        var contextFactory = new RecordingContextFactory();
        await using var node = new FlowValueAssertionNode(
            new FlowValueAssertionOptions { Expression = "passed" },
            new RecordingExpressionEngine(
                evaluate: (_, context, _) => context.Variables["passed"]),
            contextFactory);
        var output = Sink(node.Output);
        var input = Score(4);

        await node.Input.SendAsync(FlowMessage.Create(input));

        var result = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        result.Kind.ShouldBe(AssertionResultKinds.Passed);
        contextFactory.Input.ShouldBeSameAs(input);
    }

    [Fact]
    public async Task Events_and_results_use_injected_clock()
    {
        var timestamp = DateTimeOffset.Parse("2026-07-19T12:00:00Z");
        await using var node = new FlowValueAssertionNode(
            new FlowValueAssertionOptions
            {
                Expression = "assert",
                ExpressionId = "assert-v2"
            },
            new RecordingExpressionEngine(evaluate: (_, _, _) => false),
            clock: new FakeTimeProvider(timestamp));
        var output = Sink(node.Output);
        var events = Sink(node.Events);

        await node.Input.SendAsync(FlowMessage.Create(Score(5)));

        var result = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        var @event = await events.ReceiveAsync().WaitAsync(Timeout);
        result.Timestamp.ShouldBe(timestamp);
        result.Value.ShouldNotBeNull().EvaluatedAt.ShouldBe(timestamp);
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
        await using var node = new FlowValueAssertionNode(
            new FlowValueAssertionOptions { Expression = "assert" },
            new RecordingExpressionEngine(evaluate: (_, _, _) => true));
        var output = new BufferBlock<FlowMessage<FlowResult<FlowValueAssertionResult>>>();
        node.Output.LinkTo(output, new DataflowLinkOptions { PropagateCompletion = true });

        node.Complete();

        await node.Completion.WaitAsync(Timeout);
        await output.Completion.WaitAsync(Timeout);
    }

    [Fact]
    public void Canonical_node_rejects_invalid_options_and_has_no_legacy_ports()
    {
        var engine = new RecordingExpressionEngine(evaluate: (_, _, _) => true);
        Should.Throw<ArgumentException>(() =>
            new FlowValueAssertionNode(new FlowValueAssertionOptions(), engine))
            .Message.ShouldContain("expression");
        Should.Throw<ArgumentException>(() =>
            new FlowValueAssertionNode(
                new FlowValueAssertionOptions { Expression = "assert", InputType = " " },
                engine))
            .Message.ShouldContain("inputType");
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new FlowValueAssertionNode(
                new FlowValueAssertionOptions { Expression = "assert", BoundedCapacity = 0 },
                engine))
            .Message.ShouldContain("boundedCapacity");
        typeof(FlowValueAssertionNode).GetProperty("Passed").ShouldBeNull();
        typeof(FlowValueAssertionNode).GetProperty("Failed").ShouldBeNull();
        typeof(FlowValueAssertionNode).GetProperty("Errors").ShouldBeNull();
    }

    private static FlowValue Score(long score)
        => FlowValue.FromObject(new Dictionary<string, FlowValue>
        {
            ["score"] = FlowValue.From(score)
        });

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink);
        return sink;
    }

    private sealed class RecordingContextFactory : IFlowMapContextFactory<FlowValue>
    {
        public FlowValue? Input { get; private set; }

        public FlowMapContext Create(FlowValue input)
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
}
