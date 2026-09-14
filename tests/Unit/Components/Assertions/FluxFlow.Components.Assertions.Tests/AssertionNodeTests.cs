using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Assertions.Contracts;
using FluxFlow.Components.Assertions.Diagnostics;
using FluxFlow.Components.Assertions.Nodes;
using FluxFlow.Components.Assertions.Options;
using FluxFlow.Core;
using FluxFlow.Data;
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
        var output = SinkResults<AssertionResult<ScoreInput>>(node.Output);
        var highValue = new ScoreInput(12);
        var lowValue = new ScoreInput(3);
        var high = Message(highValue, new CorrelationId("assert-passed"));
        var low = Message(lowValue, new CorrelationId("assert-failed"));

        await node.Input.SendAsync(high);
        await node.Input.SendAsync(low);

        var passed = await output.ReceiveAsync().WaitAsync(Timeout);
        var failed = await output.ReceiveAsync().WaitAsync(Timeout);
        passed.IsError.ShouldBeFalse();
        passed.Value.Passed.ShouldBeTrue();
        passed.Value.Input.ShouldBe(highValue);
        passed.Value.Description.ShouldBe("score-check");
        passed.CorrelationId.ShouldBe(high.CorrelationId);
        passed.TraceId.ShouldBe(high.TraceId);
        passed.CausationId.ShouldBe(high.MessageId);
        passed.MessageId.ShouldNotBe(high.MessageId);
        failed.IsError.ShouldBeFalse();
        failed.Value.Passed.ShouldBeFalse();
        failed.Value.Input.ShouldBe(lowValue);
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
        var output = SinkResults<AssertionResult<ScoreInput>>(node.Output);

        await node.Input.SendAsync(Message(new ScoreInput(1)));
        await node.Input.SendAsync(Message(new ScoreInput(2)));

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
    public async Task Context_factory_receives_materialized_typed_value()
    {
        var contextFactory = new RecordingContextFactory();
        await using var node = new AssertionNode<ScoreInput>(
            new AssertionOptions { Expression = "passed" },
            new RecordingExpressionEngine(
                evaluate: (_, context, _) => context.Variables["passed"]),
            contextFactory);
        var output = SinkResults<AssertionResult<ScoreInput>>(node.Output);
        var input = new ScoreInput(4);

        await node.Input.SendAsync(Message(input));

        var result = await output.ReceiveAsync().WaitAsync(Timeout);
        result.Value.Passed.ShouldBeTrue();
        contextFactory.Input.ShouldBe(input);
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
        var output = SinkResults<AssertionResult<ScoreInput>>(node.Output);
        var events = Sink(node.Events);

        await node.Input.SendAsync(Message(new ScoreInput(5)));

        var result = await output.ReceiveAsync().WaitAsync(Timeout);
        var @event = await events.ReceiveAsync().WaitAsync(Timeout);
        result.Value.EvaluatedAt.ShouldBe(timestamp);
        @event.Timestamp.ShouldBe(timestamp);
        @event.ToFlowEvent().Name.ShouldBe(AssertionDiagnosticNames.Evaluated);
        @event.ToFlowEvent().Level.ShouldBe(FlowEventLevel.Information);
        @event.ToFlowEvent().Attributes["resultKind"].ShouldBe(AssertionResultKinds.Failed);
        @event.ToFlowEvent().Attributes["passed"].ShouldBe(false);
        @event.ToFlowEvent().Attributes["isError"].ShouldBe(false);
        @event.ToFlowEvent().Attributes["expressionId"].ShouldBe("assert-v2");
    }

    [Fact]
    public async Task Completion_propagates_to_output()
    {
        await using var node = new AssertionNode<ScoreInput>(
            new AssertionOptions { Expression = "assert" },
            new RecordingExpressionEngine(evaluate: (_, _, _) => true));
        var output = new BufferBlock<FlowMessage>();
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

        await node.Input.SendAsync(Message(1));
        await node.Input.SendAsync(Message(-1));

        var firstMessages = new[]
        {
            await first.ReceiveAsync().WaitAsync(Timeout),
            await first.ReceiveAsync().WaitAsync(Timeout)
        };
        var secondMessages = new[]
        {
            await second.ReceiveAsync().WaitAsync(Timeout),
            await second.ReceiveAsync().WaitAsync(Timeout)
        };
        firstMessages.Select(message => message.Value.ShouldNotBeNull().Json
            .Deserialize<AssertionResult<int>>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldNotBeNull().Passed).ShouldBe([true, false]);
        secondMessages[0].ShouldBeSameAs(firstMessages[0]);
        secondMessages[1].ShouldBeSameAs(firstMessages[1]);
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

    [Fact]
    public async Task Json_runtime_facade_uses_canonical_ports_and_preserves_lineage()
    {
        await using var node = new JsonAssertionNode(
            new AssertionOptions { Expression = "assert" },
            new RecordingExpressionEngine(
                evaluate: (_, context, _) =>
                    ((JsonElement)context.Variables["input"]!).GetProperty("score").GetInt32() >= 10));
        var output = Sink(node.Output);
        var request = Message((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new { score = 12 }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        await node.Input.SendAsync(request);

        var response = await output.ReceiveAsync().WaitAsync(Timeout);
        var result = response.Value!.Json.Deserialize<AssertionResult<JsonElement>>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        response.IsError.ShouldBeFalse();
        result.ShouldNotBeNull().Passed.ShouldBeTrue();
        result.Input.GetProperty("score").GetInt32().ShouldBe(12);
        response.CorrelationId.ShouldBe(request.CorrelationId);
        response.CausationId.ShouldBe(request.MessageId);
    }

    [Fact]
    public async Task Assertion_runs_as_a_direct_component_with_canonical_request_reply()
    {
        await using var application = new FlowApplication();
        var node = new AssertionNode<ScoreInput>(new AssertionOptions { Expression = "assert" },
            new RecordingExpressionEngine(evaluate: (_, context, _) =>
                ((ScoreInput)context.Variables["input"]!).Score >= 10));
        var component = application.AddWorkflow("main").Add("assertion", node);
        component.Instance.ShouldBeSameAs(node);
        await application.StartAsync();

        var reply = await application.RequestAsync(component.Input, component.Output,
            Message(new ScoreInput(12)), Timeout);

        var result = reply.Value.ShouldNotBeNull().Json.Deserialize<AssertionResult<ScoreInput>>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldNotBeNull();
        result.Passed.ShouldBeTrue();
        result.Input.ShouldBe(new ScoreInput(12));
        await application.StopAsync();
    }

    [Fact]
    public async Task Assertion_forwards_the_same_error_without_evaluating_it()
    {
        await using var node = new AssertionNode<ScoreInput>(new AssertionOptions { Expression = "assert" },
            new RecordingExpressionEngine(evaluate: (_, _, _) => throw new InvalidOperationException("Must not evaluate.")));
        var output = Sink(node.Output);
        var input = Message(1).WithError(new FlowError("input.failed", "Upstream failed.", "test"));

        (await node.Input.SendAsync(input)).ShouldBeTrue();

        (await output.ReceiveAsync().WaitAsync(Timeout)).ShouldBeSameAs(input);
    }

    [Fact]
    public async Task Assertion_reports_invalid_materialization_and_evaluates_next_input()
    {
        var calls = 0;
        await using var node = new AssertionNode<ScoreInput>(new AssertionOptions { Expression = "assert" },
            new RecordingExpressionEngine(evaluate: (_, _, _) => { calls++; return true; }));
        var output = Sink(node.Output);
        (await node.Input.SendAsync(Message("not an object"))).ShouldBeTrue();
        (await node.Input.SendAsync(Message(new ScoreInput(12)))).ShouldBeTrue();

        var error = await output.ReceiveAsync().WaitAsync(Timeout);
        var success = await output.ReceiveAsync().WaitAsync(Timeout);

        error.Error.ShouldNotBeNull().Code.ShouldBe(AssertionErrorCodeNames.EvaluationFailed);
        success.Value.ShouldNotBeNull().Json.Deserialize<AssertionResult<ScoreInput>>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldNotBeNull().Passed.ShouldBeTrue();
        calls.ShouldBe(1);
    }

    private static FlowMessage Message<T>(T value, CorrelationId? correlationId = null)
        => FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))), correlationId);

    private static BufferBlock<FlowMessage<T>> SinkResults<T>(ISourceBlock<FlowMessage> source)
    {
        var projection = new TransformBlock<FlowMessage, FlowMessage<T>>(message => message.IsError
            ? message.ConvertError<T>()
            : message.ConvertValue(message.Value.Json.Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!));
        source.LinkTo(projection, new DataflowLinkOptions { PropagateCompletion = true });
        return Sink(projection);
    }

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });
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
