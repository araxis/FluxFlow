using FluxFlow.Components.State;
using FluxFlow.Components.State.Contracts;
using FluxFlow.Components.State.Diagnostics;
using FluxFlow.Components.State.Nodes;
using FluxFlow.Components.State.Options;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.State.Tests;

// Every test news the node directly — no engine, no registry. Messages travel as
// FlowMessage<T> envelopes; the correlation id flows input -> result and onto any
// error for free.
public sealed class StateReducerNodeTests
{
    [Fact]
    public async Task Reducer_UpdatesStatePerKeyInOrderAndPreservesCorrelationId()
    {
        await using var node = new StateReducerNode(
            new StateReducerOptions { Reducer = "count" },
            new SampleExpressionEngine());
        var output = Sink(node.Output);

        var first = FlowMessage.Create(new StateReducerInput { Key = "a", Input = "first" });
        await node.Input.SendAsync(first);
        await node.Input.SendAsync(FlowMessage.Create(new StateReducerInput { Key = "a", Input = "second" }));
        await node.Input.SendAsync(FlowMessage.Create(new StateReducerInput { Key = "b", Input = "other" }));

        var firstResult = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var second = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var third = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));

        firstResult.CorrelationId.ShouldBe(first.CorrelationId);   // the whole point of the envelope
        firstResult.Payload.Key.ShouldBe("a");
        firstResult.Payload.PreviousState.ShouldBeNull();
        firstResult.Payload.NewState.ShouldBe(1);
        firstResult.Payload.Version.ShouldBe(1);
        second.Payload.Key.ShouldBe("a");
        second.Payload.PreviousState.ShouldBe(1);
        second.Payload.NewState.ShouldBe(2);
        second.Payload.Version.ShouldBe(2);
        third.Payload.Key.ShouldBe("b");
        third.Payload.NewState.ShouldBe(1);
        third.Payload.Version.ShouldBe(1);
    }

    [Fact]
    public async Task Output_FansOutEveryResultToEveryConsumer()
    {
        // One node's output linked to two downstream consumers, no engine. Both see
        // every result.
        await using var node = new StateReducerNode(
            new StateReducerOptions { Reducer = "count" },
            new SampleExpressionEngine());
        var logger = Sink(node.Output);
        var mapper = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new StateReducerInput { Key = "a" }));
        await node.Input.SendAsync(FlowMessage.Create(new StateReducerInput { Key = "a" }));

        (await logger.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.Version.ShouldBe(1);
        (await logger.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.Version.ShouldBe(2);
        (await mapper.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.Version.ShouldBe(1);
        (await mapper.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.Version.ShouldBe(2);
    }

    [Fact]
    public async Task Reducer_UsesInitialStateFromRequestOrOptions()
    {
        await using var node = new StateReducerNode(
            new StateReducerOptions { Reducer = "count", InitialState = 10 },
            new SampleExpressionEngine());
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new StateReducerInput { Key = "a", Input = "first" }));
        await node.Input.SendAsync(FlowMessage.Create(new StateReducerInput { Key = "b", InitialState = 20 }));

        var first = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload;
        var second = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload;

        first.PreviousState.ShouldBe(10);
        first.NewState.ShouldBe(11);
        second.PreviousState.ShouldBe(20);
        second.NewState.ShouldBe(21);
    }

    [Fact]
    public async Task Reducer_CanResolveKeyWithExpression()
    {
        await using var node = new StateReducerNode(
            new StateReducerOptions { KeyExpression = "topic-key", Reducer = "last-input" },
            new SampleExpressionEngine());
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new StateReducerInput
        {
            Key = "ignored",
            Input = "payload",
            Variables = new Dictionary<string, object?>
            {
                ["topic"] = "orders/created"
            }
        }));

        var result = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload;
        result.Key.ShouldBe("orders/created");
        result.NewState.ShouldBe("payload");
    }

    [Fact]
    public async Task Reducer_ResetAndClearUseSameInput()
    {
        await using var node = new StateReducerNode(
            new StateReducerOptions { Reducer = "count", InitialState = 5 },
            new SampleExpressionEngine());
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new StateReducerInput { Key = "a" }));
        await node.Input.SendAsync(FlowMessage.Create(new StateReducerInput
        {
            Key = "a",
            InitialState = 100,
            Operation = StateReducerOperation.Reset
        }));
        await node.Input.SendAsync(FlowMessage.Create(new StateReducerInput
        {
            Key = "a",
            Operation = StateReducerOperation.Clear
        }));
        await node.Input.SendAsync(FlowMessage.Create(new StateReducerInput { Key = "a" }));

        await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var reset = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload;
        var clear = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload;
        var afterClear = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload;

        reset.PreviousState.ShouldBe(6);
        reset.NewState.ShouldBe(100);
        reset.Version.ShouldBe(2);
        clear.PreviousState.ShouldBe(100);
        clear.NewState.ShouldBeNull();
        clear.Version.ShouldBe(3);
        afterClear.PreviousState.ShouldBe(5);
        afterClear.NewState.ShouldBe(6);
        afterClear.Version.ShouldBe(1);
    }

    [Fact]
    public async Task Reducer_UsesConfiguredClockForResults()
    {
        var fixedInstant = new DateTimeOffset(2026, 6, 2, 18, 45, 0, TimeSpan.Zero);
        await using var node = new StateReducerNode(
            new StateReducerOptions { Reducer = "count", InitialState = 5 },
            new SampleExpressionEngine(),
            new FakeTimeProvider(fixedInstant));
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new StateReducerInput { Key = "a" }));
        await node.Input.SendAsync(FlowMessage.Create(new StateReducerInput
        {
            Key = "a",
            InitialState = 100,
            Operation = StateReducerOperation.Reset
        }));
        await node.Input.SendAsync(FlowMessage.Create(new StateReducerInput
        {
            Key = "a",
            Operation = StateReducerOperation.Clear
        }));

        var reduce = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload;
        var reset = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload;
        var clear = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload;

        reduce.UpdatedAt.ShouldBe(fixedInstant);
        reset.UpdatedAt.ShouldBe(fixedInstant);
        clear.UpdatedAt.ShouldBe(fixedInstant);
    }

    [Fact]
    public async Task Reducer_ReportsReducerFailuresWithCorrelationIdAndContinues()
    {
        await using var node = new StateReducerNode(
            new StateReducerOptions { Reducer = "fail-on-bad" },
            new SampleExpressionEngine());
        var output = Sink(node.Output);
        var errors = Sink(node.Errors);

        var bad = FlowMessage.Create(new StateReducerInput { Key = "a", Input = "bad" });
        await node.Input.SendAsync(bad);
        await node.Input.SendAsync(FlowMessage.Create(new StateReducerInput { Key = "a", Input = "good" }));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(StateErrorCodes.ReducerFailed);
        error.CorrelationId.ShouldBe(bad.CorrelationId);

        // The pump keeps going: the second (well-formed) message still reduces.
        var result = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload;
        result.NewState.ShouldBe("good");
        result.Version.ShouldBe(1);
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Reducer_RespectsMaxKeyLimit()
    {
        await using var node = new StateReducerNode(
            new StateReducerOptions { Reducer = "count", MaxKeys = 1 },
            new SampleExpressionEngine());
        var output = Sink(node.Output);
        var errors = Sink(node.Errors);

        await node.Input.SendAsync(FlowMessage.Create(new StateReducerInput { Key = "a" }));
        var rejected = FlowMessage.Create(new StateReducerInput { Key = "b" });
        await node.Input.SendAsync(rejected);

        var result = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload;
        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));

        result.Key.ShouldBe("a");
        error.Code.ShouldBe(StateErrorCodes.KeyLimitReached);
        error.CorrelationId.ShouldBe(rejected.CorrelationId);
    }

    [Fact]
    public async Task Reducer_CapsItemizedRejectedKeyWarnings()
    {
        await using var node = new StateReducerNode(
            new StateReducerOptions { Reducer = "count", MaxKeys = 1 },
            new SampleExpressionEngine());
        var output = Sink(node.Output);
        var errors = new BufferBlock<FlowError>();
        node.Errors.LinkTo(errors, new DataflowLinkOptions { PropagateCompletion = true });
        var events = new BufferBlock<FlowEvent>();
        node.Events.LinkTo(events, new DataflowLinkOptions { PropagateCompletion = true });

        await node.Input.SendAsync(FlowMessage.Create(new StateReducerInput { Key = "tracked" }));
        for (var index = 0; index < 1100; index++)
        {
            await node.Input.SendAsync(FlowMessage.Create(new StateReducerInput { Key = $"rejected-{index}" }));
        }

        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.Key.ShouldBe("tracked");
        var keyLimitEvents = (await DrainUntilCompletedAsync(events))
            .Where(@event => @event.Name == StateDiagnosticNames.KeyLimitReached)
            .ToList();
        keyLimitEvents.Count.ShouldBe(1025);
        keyLimitEvents
            .Count(@event => @event.Message!.Contains("will not be itemized"))
            .ShouldBe(1);
        keyLimitEvents.ShouldAllBe(@event => @event.Level == FlowEventLevel.Warning);
        (await DrainUntilCompletedAsync(errors)).Count.ShouldBe(1100);
    }

    [Fact]
    public async Task Reducer_EmitsEventsCarryingCorrelationId()
    {
        await using var node = new StateReducerNode(
            new StateReducerOptions { Reducer = "count", ExpressionName = "counter" },
            new SampleExpressionEngine());
        var output = Sink(node.Output);
        var events = Sink(node.Events);

        var message = FlowMessage.Create(new StateReducerInput { Key = "a" });
        await node.Input.SendAsync(message);
        await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));

        var @event = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        @event.Name.ShouldBe(StateDiagnosticNames.ReducerUpdated);
        @event.Level.ShouldBe(FlowEventLevel.Information);
        @event.CorrelationId.ShouldBe(message.CorrelationId);
        @event.Attributes["key"].ShouldBe("a");
        @event.Attributes["version"].ShouldBe(1L);
        @event.Attributes["engine"].ShouldBe("sample");
        @event.Attributes["expressionName"].ShouldBe("counter");
    }

    [Fact]
    public void Reducer_RejectsMissingReducer()
    {
        var exception = Should.Throw<ArgumentException>(
            () => new StateReducerNode(
                new StateReducerOptions { Reducer = "", BoundedCapacity = 1 },
                new SampleExpressionEngine()));

        exception.Message.ShouldContain("reducer");
    }

    [Fact]
    public void Reducer_RejectsEmptyKeyExpression()
    {
        var exception = Should.Throw<ArgumentException>(
            () => new StateReducerNode(
                new StateReducerOptions { Reducer = "count", KeyExpression = " " },
                new SampleExpressionEngine()));

        exception.Message.ShouldContain("keyExpression");
    }

    [Fact]
    public void Reducer_RejectsInvalidBoundedCapacity()
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => new StateReducerNode(
                new StateReducerOptions { Reducer = "count", BoundedCapacity = 0 },
                new SampleExpressionEngine()));

        exception.Message.ShouldContain("boundedCapacity");
    }

    [Fact]
    public void Reducer_RejectsInvalidMaxKeys()
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => new StateReducerNode(
                new StateReducerOptions { Reducer = "count", MaxKeys = -1 },
                new SampleExpressionEngine()));

        exception.Message.ShouldContain("maxKeys");
    }

    [Fact]
    public void Constructor_RequiresExpressionEngine()
        => Should.Throw<ArgumentNullException>(
            () => new StateReducerNode(new StateReducerOptions { Reducer = "count" }, null!));

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = false });
        return sink;
    }

    private static async Task<List<T>> DrainUntilCompletedAsync<T>(BufferBlock<T> source)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var entries = new List<T>();
        while (await source.OutputAvailableAsync(cancellation.Token))
        {
            while (source.TryReceive(out var entry))
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    private sealed class SampleExpressionEngine : IFlowExpressionEngine
    {
        public string Name => "sample";

        public object? Evaluate(
            string expression,
            FlowMapContext context,
            Type resultType)
            => expression switch
            {
                "count" => CoerceNumber(context.Variables["state"]) + 1,
                "last-input" => context.Variables["input"],
                "topic-key" => context.Variables["topic"],
                "fail-on-bad" when Equals(context.Variables["input"], "bad") =>
                    throw new InvalidOperationException("bad input"),
                "fail-on-bad" => context.Variables["input"],
                _ => throw new InvalidOperationException($"Unknown expression '{expression}'.")
            };

        private static long CoerceNumber(object? value)
            => value switch
            {
                null => 0,
                long number => number,
                int number => number,
                JsonElement json when json.ValueKind == JsonValueKind.Number &&
                                      json.TryGetInt64(out var number) => number,
                _ => throw new InvalidOperationException(
                    $"Cannot coerce '{value.GetType().Name}' to a number.")
            };
    }
}
