using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.State.Contracts;
using FluxFlow.Components.State.Diagnostics;
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
        var other = FlowMessage.Create(Command("other", 4));

        await node.Input.SendAsync(first);
        await node.Input.SendAsync(second);
        await node.Input.SendAsync(other);

        var firstResult = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        var secondResult = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        var otherResult = await results.ReceiveAsync().WaitAsync(WaitTimeout);

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

        otherResult.Payload.Value.ShouldNotBeNull().Key.ShouldBe("other");
        otherResult.Payload.Value.PreviousState.GetInteger().ShouldBe(0);
        otherResult.Payload.Value.NewState.GetInteger().ShouldBe(4);
        otherResult.Payload.Value.Version.ShouldBe(1);
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
        await node.Input.SendAsync(FlowMessage.Create(Command("counter", 4)));

        var reset = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        var cleared = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        var afterClear = await results.ReceiveAsync().WaitAsync(WaitTimeout);

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

        afterClear.Payload.Value.ShouldNotBeNull().PreviousState.GetInteger().ShouldBe(1);
        afterClear.Payload.Value.NewState.GetInteger().ShouldBe(5);
        afterClear.Payload.Value.Version.ShouldBe(1);
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
        failure.Payload.Error.Details.GetObject().ContainsKey("legacyCode").ShouldBeFalse();
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
    public async Task Output_fans_out_every_result_to_every_consumer()
    {
        await using var node = new FlowValueStateReducerNode(
            Options("sum") with { InitialState = FlowValue.From(0) },
            new FlowValueExpressionEngine());
        var firstConsumer = Link(node.Output);
        var secondConsumer = Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(Command("counter", 1)));
        await node.Input.SendAsync(FlowMessage.Create(Command("counter", 2)));

        (await firstConsumer.ReceiveAsync().WaitAsync(WaitTimeout))
            .Payload.Value.ShouldNotBeNull().Version.ShouldBe(1);
        (await firstConsumer.ReceiveAsync().WaitAsync(WaitTimeout))
            .Payload.Value.ShouldNotBeNull().Version.ShouldBe(2);
        (await secondConsumer.ReceiveAsync().WaitAsync(WaitTimeout))
            .Payload.Value.ShouldNotBeNull().Version.ShouldBe(1);
        (await secondConsumer.ReceiveAsync().WaitAsync(WaitTimeout))
            .Payload.Value.ShouldNotBeNull().Version.ShouldBe(2);
    }

    [Fact]
    public async Task Command_initial_state_overrides_the_option_for_a_new_key()
    {
        await using var node = new FlowValueStateReducerNode(
            Options("sum") with { InitialState = FlowValue.From(10) },
            new FlowValueExpressionEngine());
        var results = Link(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new FlowValueStateReducerInput
        {
            Key = "counter",
            Input = FlowValue.From(2),
            InitialState = FlowValue.From(20)
        }));

        var result = (await results.ReceiveAsync().WaitAsync(WaitTimeout)).Payload.Value
            .ShouldNotBeNull();
        result.PreviousState.GetInteger().ShouldBe(20);
        result.NewState.GetInteger().ShouldBe(22);
    }

    [Fact]
    public async Task Unsupported_operation_is_a_normal_result_and_later_input_continues()
    {
        await using var node = new FlowValueStateReducerNode(
            Options("sum") with { InitialState = FlowValue.From(0) },
            new FlowValueExpressionEngine());
        var results = Link(node.Output);
        var invalid = FlowMessage.Create(new FlowValueStateReducerInput
        {
            Key = "counter",
            Operation = (StateReducerOperation)999
        });

        await node.Input.SendAsync(invalid);
        await node.Input.SendAsync(FlowMessage.Create(Command("counter", 3)));

        var failure = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        var success = await results.ReceiveAsync().WaitAsync(WaitTimeout);
        failure.CorrelationId.ShouldBe(invalid.CorrelationId);
        failure.Payload.Error.ShouldNotBeNull().Code.ShouldBe(StateErrorCodeNames.InvalidMessage);
        success.Payload.Value.ShouldNotBeNull().NewState.GetInteger().ShouldBe(3);
        success.Payload.Value.Version.ShouldBe(1);
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Key_limit_caps_itemized_warning_events()
    {
        await using var node = new FlowValueStateReducerNode(
            Options("last-input") with { MaxKeys = 1 },
            new FlowValueExpressionEngine());
        var results = Link(node.Output);
        var events = Link(node.Events);

        await node.Input.SendAsync(FlowMessage.Create(Command("tracked", 0)));
        for (var index = 0; index < 1100; index++)
            await node.Input.SendAsync(FlowMessage.Create(Command($"rejected-{index}", index)));

        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await DrainUntilCompletedAsync(results)).Count.ShouldBe(1101);
        var warnings = (await DrainUntilCompletedAsync(events))
            .Where(@event => @event.Name == StateDiagnosticNames.KeyLimitReached)
            .ToList();
        warnings.Count.ShouldBe(1025);
        warnings.Count(@event => @event.Message!.Contains("will not be itemized"))
            .ShouldBe(1);
        warnings.ShouldAllBe(@event => @event.Level == FlowEventLevel.Warning);
    }

    [Fact]
    public async Task Successful_operation_emits_correlated_diagnostic_metadata()
    {
        await using var node = new FlowValueStateReducerNode(
            Options("last-input") with { ExpressionName = "latest value" },
            new FlowValueExpressionEngine());
        var results = Link(node.Output);
        var events = Link(node.Events);
        var message = FlowMessage.Create(Command("counter", 4));

        await node.Input.SendAsync(message);
        await results.ReceiveAsync().WaitAsync(WaitTimeout);

        var @event = await events.ReceiveAsync().WaitAsync(WaitTimeout);
        @event.CorrelationId.ShouldBe(message.CorrelationId);
        @event.Name.ShouldBe(StateDiagnosticNames.ReducerUpdated);
        @event.Attributes["engine"].ShouldBe("flow-value");
        @event.Attributes["expressionName"].ShouldBe("latest value");
        @event.Attributes["key"].ShouldBe("counter");
        @event.Attributes["version"].ShouldBe(1L);
    }

    [Fact]
    public void Constructor_requires_an_expression_engine()
        => Should.Throw<ArgumentNullException>(() => new FlowValueStateReducerNode(
            Options("last-input"),
            null!));

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

    private static async Task<List<T>> DrainUntilCompletedAsync<T>(BufferBlock<T> source)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var entries = new List<T>();
        while (await source.OutputAvailableAsync(cancellation.Token))
        {
            while (source.TryReceive(out var entry))
                entries.Add(entry);
        }

        return entries;
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
