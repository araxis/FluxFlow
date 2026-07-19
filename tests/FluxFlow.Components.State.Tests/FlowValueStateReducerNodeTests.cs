using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.State.Contracts;
using FluxFlow.Components.State.Nodes;
using FluxFlow.Components.State.Options;
using FluxFlow.Data;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.State.Tests;

public sealed class FlowValueStateReducerNodeTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Reduce_preserves_order_state_and_message_lineage()
    {
        var timestamp = DateTimeOffset.Parse("2026-07-19T14:00:00Z");
        await using var node = new FlowValueStateReducerNode(
            Options("sum") with { InitialState = FlowValue.From(0) },
            new FlowValueExpressionEngine(),
            new FakeTimeProvider(timestamp));
        var results = Link(node.Output);
        var first = FlowMessage.Create(Command("counter", 2));
        var second = FlowMessage.Create(Command("counter", 3));

        await node.Input.SendAsync(first);
        await node.Input.SendAsync(second);

        var firstResult = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        var secondResult = await results.ReceiveAsync().WaitAsync(WaitTimeout);

        firstResult.Payload.Kind.ShouldBe(StateResultKinds.Updated);
        firstResult.Payload.IsError.ShouldBeFalse();
        firstResult.Payload.Value.ShouldNotBeNull().PreviousState.GetInteger().ShouldBe(0);
        firstResult.Payload.Value.NewState.GetInteger().ShouldBe(2);
        firstResult.Payload.Value.Version.ShouldBe(1);
        firstResult.Payload.Value.UpdatedAt.ShouldBe(timestamp);
        firstResult.CorrelationId.ShouldBe(first.CorrelationId);
        firstResult.TraceId.ShouldBe(first.TraceId);
        firstResult.CausationId.ShouldBe(first.MessageId);

        secondResult.Payload.Value.ShouldNotBeNull().PreviousState.GetInteger().ShouldBe(2);
        secondResult.Payload.Value.NewState.GetInteger().ShouldBe(5);
        secondResult.Payload.Value.Version.ShouldBe(2);
        secondResult.CorrelationId.ShouldBe(second.CorrelationId);
    }

    [Fact]
    public async Task Reset_and_clear_are_successful_domain_results()
    {
        await using var node = new FlowValueStateReducerNode(
            Options("sum") with { InitialState = FlowValue.From(1) },
            new FlowValueExpressionEngine());
        var results = Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(Command("counter", 2)));
        await results.ReceiveAsync().WaitAsync(WaitTimeout);
        await node.Input.SendAsync(FlowMessage.Create(new FlowValueStateReducerInput
        {
            Key = "counter",
            InitialState = FlowValue.From(10),
            Operation = StateReducerOperation.Reset
        }));
        await node.Input.SendAsync(FlowMessage.Create(new FlowValueStateReducerInput
        {
            Key = "counter",
            Operation = StateReducerOperation.Clear
        }));

        var reset = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        var cleared = await results.ReceiveAsync().WaitAsync(WaitTimeout);

        reset.Payload.Kind.ShouldBe(StateResultKinds.Reset);
        reset.Payload.Value.ShouldNotBeNull().PreviousState.GetInteger().ShouldBe(3);
        reset.Payload.Value.NewState.GetInteger().ShouldBe(10);
        reset.Payload.Value.Operation.ShouldBe(StateReducerOperation.Reset);
        reset.Payload.Value.Version.ShouldBe(2);

        cleared.Payload.Kind.ShouldBe(StateResultKinds.Cleared);
        cleared.Payload.Value.ShouldNotBeNull().PreviousState.GetInteger().ShouldBe(10);
        cleared.Payload.Value.NewState.ShouldBe(FlowValue.Null);
        cleared.Payload.Value.Operation.ShouldBe(StateReducerOperation.Clear);
        cleared.Payload.Value.Version.ShouldBe(3);
    }

    [Fact]
    public async Task Reducer_failure_is_normal_result_and_later_input_continues()
    {
        await using var node = new FlowValueStateReducerNode(
            Options("fail-negative") with { InitialState = FlowValue.From(0) },
            new FlowValueExpressionEngine());
        var results = Link(node.Output);
        var rejected = FlowMessage.Create(Command("counter", -1));

        await node.Input.SendAsync(rejected);
        await node.Input.SendAsync(FlowMessage.Create(Command("counter", 4)));

        var failure = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        var success = await results.ReceiveAsync().WaitAsync(WaitTimeout);

        failure.Payload.Kind.ShouldBe(StateResultKinds.OperationFailed);
        failure.Payload.IsError.ShouldBeTrue();
        failure.Payload.Error.ShouldNotBeNull().Code.ShouldBe(StateErrorCodeNames.ReducerFailed);
        failure.Payload.Error.Details.GetObject()["legacyCode"].GetInteger()
            .ShouldBe(StateErrorCodes.ReducerFailed);
        failure.CorrelationId.ShouldBe(rejected.CorrelationId);

        success.Payload.Kind.ShouldBe(StateResultKinds.Updated);
        success.Payload.Value.ShouldNotBeNull().PreviousState.GetInteger().ShouldBe(0);
        success.Payload.Value.NewState.GetInteger().ShouldBe(4);
        success.Payload.Value.Version.ShouldBe(1);
    }

    [Fact]
    public async Task Key_expression_and_key_limit_use_flow_value_data()
    {
        await using var node = new FlowValueStateReducerNode(
            Options("last-input") with
            {
                KeyExpression = "variable-key",
                MaxKeys = 1
            },
            new FlowValueExpressionEngine());
        var results = Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(Command("ignored", 1, "first")));
        await node.Input.SendAsync(FlowMessage.Create(Command("ignored", 2, "second")));

        var first = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        var second = await results.ReceiveAsync().WaitAsync(WaitTimeout);

        first.Payload.Value.ShouldNotBeNull(first.Payload.Error?.Message).Key.ShouldBe("first");
        first.Payload.Value.NewState.GetInteger().ShouldBe(1);
        second.Payload.IsError.ShouldBeTrue();
        second.Payload.Error.ShouldNotBeNull().Code.ShouldBe(StateErrorCodeNames.KeyLimitReached);
    }

    [Fact]
    public async Task Missing_command_is_normal_invalid_message_result()
    {
        await using var node = new FlowValueStateReducerNode(
            Options("last-input"),
            new FlowValueExpressionEngine());
        var results = Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create<FlowValueStateReducerInput>(null!));

        var failure = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        failure.Payload.IsError.ShouldBeTrue();
        failure.Payload.Error.ShouldNotBeNull().Code.ShouldBe(StateErrorCodeNames.InvalidMessage);
    }

    [Fact]
    public void Input_variables_are_copied_with_ordinal_keys()
    {
        var variables = new Dictionary<string, FlowValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = FlowValue.From("before")
        };
        var input = new FlowValueStateReducerInput
        {
            Key = "key",
            Variables = variables
        };

        variables["Name"] = FlowValue.From("after");

        input.Variables["Name"].GetString().ShouldBe("before");
        input.Variables.ContainsKey("name").ShouldBeFalse();
    }

    private static FlowValueStateReducerOptions Options(string reducer)
        => new() { Reducer = reducer };

    private static FlowValueStateReducerInput Command(
        string key,
        long input,
        string? variableKey = null)
        => new()
        {
            Key = key,
            Input = FlowValue.From(input),
            Variables = variableKey is null
                ? new Dictionary<string, FlowValue>(StringComparer.Ordinal)
                : new Dictionary<string, FlowValue>(StringComparer.Ordinal)
                {
                    ["group"] = FlowValue.From(variableKey)
                }
        };

    private static BufferBlock<T> Link<T>(ISourceBlock<T> source)
    {
        var buffer = new BufferBlock<T>();
        source.LinkTo(buffer, new DataflowLinkOptions { PropagateCompletion = true });
        return buffer;
    }

    private sealed class FlowValueExpressionEngine : IFlowExpressionEngine
    {
        public string Name => "flow-value";

        public object? Evaluate(string expression, FlowMapContext context, Type resultType)
        {
            var input = (FlowValue)context.Variables["input"]!;
            var state = (FlowValue)context.Variables["state"]!;
            return expression switch
            {
                "sum" => FlowValue.From(state.GetInteger() + input.GetInteger()),
                "last-input" => input,
                "fail-negative" when input.GetInteger() < 0 =>
                    throw new InvalidOperationException("negative input"),
                "fail-negative" => FlowValue.From(state.GetInteger() + input.GetInteger()),
                "variable-key" => (object)((FlowValue)context.Variables["group"]!).GetString(),
                _ => throw new InvalidOperationException($"Unknown expression '{expression}'.")
            };
        }
    }
}
