using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.State.Contracts;
using FluxFlow.Components.State.Diagnostics;
using FluxFlow.Components.State.Nodes;
using FluxFlow.Components.State.Options;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.State.Tests;

public sealed class StateReducerNodeTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Reduce_preserves_order_typed_state_and_message_lineage()
    {
        var timestamp = DateTimeOffset.Parse("2026-07-19T14:00:00Z");
        await using var node = new StateReducerNode<long?>(
            Options("sum") with { InitialState = 0 },
            new NumericExpressionEngine(),
            new FakeTimeProvider(timestamp));
        var results = Link(node.Output);
        var first = FlowMessage.Create(Command("counter", 2));
        var second = FlowMessage.Create(Command("counter", 3));
        var other = FlowMessage.Create(Command("other", 4));

        await node.Input.SendAsync(first);
        await node.Input.SendAsync(second);
        await node.Input.SendAsync(other);

        var firstResult = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        var secondResult = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        var otherResult = await results.ReceiveAsync().WaitAsync(WaitTimeout);

        firstResult.IsError.ShouldBeFalse();
        firstResult.Value.PreviousState.ShouldBe(0);
        firstResult.Value.NewState.ShouldBe(2);
        firstResult.Value.Version.ShouldBe(1);
        firstResult.Value.UpdatedAt.ShouldBe(timestamp);
        firstResult.CorrelationId.ShouldBe(first.CorrelationId);
        firstResult.TraceId.ShouldBe(first.TraceId);
        firstResult.CausationId.ShouldBe(first.MessageId);

        secondResult.Value.PreviousState.ShouldBe(2);
        secondResult.Value.NewState.ShouldBe(5);
        secondResult.Value.Version.ShouldBe(2);
        secondResult.CorrelationId.ShouldBe(second.CorrelationId);

        otherResult.Value.Key.ShouldBe("other");
        otherResult.Value.PreviousState.ShouldBe(0);
        otherResult.Value.NewState.ShouldBe(4);
        otherResult.Value.Version.ShouldBe(1);
    }

    [Fact]
    public async Task Reset_and_clear_are_successful_domain_results()
    {
        await using var node = new StateReducerNode<long?>(
            Options("sum") with { InitialState = 1 },
            new NumericExpressionEngine());
        var results = Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(Command("counter", 2)));
        await results.ReceiveAsync().WaitAsync(WaitTimeout);
        await node.Input.SendAsync(FlowMessage.Create(new StateReducerInput<long?>
        {
            Key = "counter",
            InitialState = 10,
            Operation = StateReducerOperation.Reset
        }));
        await node.Input.SendAsync(FlowMessage.Create(new StateReducerInput<long?>
        {
            Key = "counter",
            Operation = StateReducerOperation.Clear
        }));
        await node.Input.SendAsync(FlowMessage.Create(Command("counter", 4)));

        var reset = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        var cleared = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        var afterClear = await results.ReceiveAsync().WaitAsync(WaitTimeout);

        reset.Value.PreviousState.ShouldBe(3);
        reset.Value.NewState.ShouldBe(10);
        reset.Value.Operation.ShouldBe(StateReducerOperation.Reset);
        reset.Value.Version.ShouldBe(2);

        cleared.Value.PreviousState.ShouldBe(10);
        cleared.Value.NewState.ShouldBeNull();
        cleared.Value.Operation.ShouldBe(StateReducerOperation.Clear);
        cleared.Value.Version.ShouldBe(3);

        afterClear.Value.PreviousState.ShouldBe(1);
        afterClear.Value.NewState.ShouldBe(5);
        afterClear.Value.Version.ShouldBe(1);
    }

    [Fact]
    public async Task Reducer_failure_is_in_band_and_later_input_continues()
    {
        await using var node = new StateReducerNode<long?>(
            Options("fail-negative") with { InitialState = 0 },
            new NumericExpressionEngine());
        var results = Link(node.Output);
        var rejected = FlowMessage.Create(Command("counter", -1));

        await node.Input.SendAsync(rejected);
        await node.Input.SendAsync(FlowMessage.Create(Command("counter", 4)));

        var failure = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        var success = await results.ReceiveAsync().WaitAsync(WaitTimeout);

        failure.IsError.ShouldBeTrue();
        failure.Error!.Code.ShouldBe(StateErrorCodeNames.ReducerFailed);
        failure.CorrelationId.ShouldBe(rejected.CorrelationId);
        success.Value.PreviousState.ShouldBe(0);
        success.Value.NewState.ShouldBe(4);
        success.Value.Version.ShouldBe(1);
    }

    [Fact]
    public async Task Key_expression_and_key_limit_use_typed_input_variables()
    {
        await using var node = new StateReducerNode<long?>(
            Options("last-input") with { KeyExpression = "variable-key", MaxKeys = 1 },
            new NumericExpressionEngine());
        var results = Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(Command("ignored", 1, "first")));
        await node.Input.SendAsync(FlowMessage.Create(Command("ignored", 2, "second")));

        var first = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        var second = await results.ReceiveAsync().WaitAsync(WaitTimeout);

        first.Value.Key.ShouldBe("first");
        first.Value.NewState.ShouldBe(1);
        second.IsError.ShouldBeTrue();
        second.Error!.Code.ShouldBe(StateErrorCodeNames.KeyLimitReached);
    }

    [Fact]
    public async Task Incoming_error_is_propagated()
    {
        await using var node = new StateReducerNode<long?>(
            Options("last-input"),
            new NumericExpressionEngine());
        var results = Link(node.Output);
        var error = new FluxFlow.Data.FlowError(
            "upstream.failed",
            "Input was unavailable.",
            "State",
            isTransient: false);

        await node.Input.SendAsync(FlowMessage.CreateError<StateReducerInput<long?>>(error));

        var failure = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        failure.IsError.ShouldBeTrue();
        failure.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public async Task Output_fans_out_every_result_to_every_consumer()
    {
        await using var node = new StateReducerNode<long?>(
            Options("sum") with { InitialState = 0 },
            new NumericExpressionEngine());
        var firstConsumer = Link(node.Output);
        var secondConsumer = Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(Command("counter", 1)));
        await node.Input.SendAsync(FlowMessage.Create(Command("counter", 2)));

        (await firstConsumer.ReceiveAsync().WaitAsync(WaitTimeout)).Value.Version.ShouldBe(1);
        (await firstConsumer.ReceiveAsync().WaitAsync(WaitTimeout)).Value.Version.ShouldBe(2);
        (await secondConsumer.ReceiveAsync().WaitAsync(WaitTimeout)).Value.Version.ShouldBe(1);
        (await secondConsumer.ReceiveAsync().WaitAsync(WaitTimeout)).Value.Version.ShouldBe(2);
    }

    [Fact]
    public async Task Command_initial_state_overrides_the_option_for_a_new_key()
    {
        await using var node = new StateReducerNode<long?>(
            Options("sum") with { InitialState = 10 },
            new NumericExpressionEngine());
        var results = Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new StateReducerInput<long?>
        {
            Key = "counter",
            Input = 2,
            InitialState = 20
        }));

        var result = (await results.ReceiveAsync().WaitAsync(WaitTimeout)).Value;
        result.PreviousState.ShouldBe(20);
        result.NewState.ShouldBe(22);
    }

    [Fact]
    public async Task Unsupported_operation_is_in_band_and_later_input_continues()
    {
        await using var node = new StateReducerNode<long?>(
            Options("sum") with { InitialState = 0 },
            new NumericExpressionEngine());
        var results = Link(node.Output);
        var invalid = FlowMessage.Create(new StateReducerInput<long?>
        {
            Key = "counter",
            Operation = (StateReducerOperation)999
        });

        await node.Input.SendAsync(invalid);
        await node.Input.SendAsync(FlowMessage.Create(Command("counter", 3)));

        var failure = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        var success = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        failure.CorrelationId.ShouldBe(invalid.CorrelationId);
        failure.Error!.Code.ShouldBe(StateErrorCodeNames.InvalidMessage);
        success.Value.NewState.ShouldBe(3);
        success.Value.Version.ShouldBe(1);
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Successful_operation_emits_correlated_diagnostic_metadata()
    {
        await using var node = new StateReducerNode<long?>(
            Options("last-input") with { ExpressionName = "latest value" },
            new NumericExpressionEngine());
        var results = Link(node.Output);
        var events = Link(node.Events);
        var message = FlowMessage.Create(Command("counter", 4));

        await node.Input.SendAsync(message);
        await results.ReceiveAsync().WaitAsync(WaitTimeout);

        var @event = await events.ReceiveAsync().WaitAsync(WaitTimeout);
        @event.CorrelationId.ShouldBe(message.CorrelationId);
        @event.Name.ShouldBe(StateDiagnosticNames.ReducerUpdated);
        @event.Attributes["engine"].ShouldBe("numeric");
        @event.Attributes["expressionName"].ShouldBe("latest value");
        @event.Attributes["key"].ShouldBe("counter");
        @event.Attributes["version"].ShouldBe(1L);
    }

    [Fact]
    public void Constructor_requires_an_expression_engine()
        => Should.Throw<ArgumentNullException>(() => new StateReducerNode<long?>(
            Options("last-input"),
            null!));

    [Fact]
    public void Input_variables_are_copied_with_ordinal_keys()
    {
        var variables = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = "before"
        };
        var input = new StateReducerInput<long?> { Key = "key", Variables = variables };

        variables["Name"] = "after";

        input.Variables["Name"].ShouldBe("before");
        input.Variables.ContainsKey("name").ShouldBeFalse();
    }

    private static StateReducerOptions<long?> Options(string reducer)
        => new() { Reducer = reducer };

    private static StateReducerInput<long?> Command(
        string key,
        long input,
        string? variableKey = null)
        => new()
        {
            Key = key,
            Input = input,
            Variables = variableKey is null
                ? new Dictionary<string, object?>(StringComparer.Ordinal)
                : new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["group"] = variableKey
                }
        };

    private static BufferBlock<T> Link<T>(ISourceBlock<T> source)
    {
        var buffer = new BufferBlock<T>();
        source.LinkTo(buffer, new DataflowLinkOptions { PropagateCompletion = true });
        return buffer;
    }

    private sealed class NumericExpressionEngine : IFlowExpressionEngine
    {
        public string Name => "numeric";

        public object? Evaluate(string expression, FlowMapContext context, Type resultType)
        {
            var input = Convert.ToInt64(context.Variables["input"]);
            var state = Convert.ToInt64(context.Variables["state"]);
            return expression switch
            {
                "sum" => state + input,
                "last-input" => input,
                "fail-negative" when input < 0 =>
                    throw new InvalidOperationException("negative input"),
                "fail-negative" => state + input,
                "variable-key" => context.Variables["group"],
                _ => throw new InvalidOperationException($"Unknown expression '{expression}'.")
            };
        }
    }
}
