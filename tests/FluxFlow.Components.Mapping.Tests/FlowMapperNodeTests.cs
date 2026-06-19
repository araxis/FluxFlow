using FluxFlow.Components.Mapping.Contracts;
using FluxFlow.Components.Mapping.Nodes;
using FluxFlow.Components.Mapping.Options;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Mapping.Tests;

// Every test news the node directly — no engine, no registry. Messages travel as
// FlowMessage<T> envelopes; the correlation id flows input -> output, onto the
// Failed branch, and onto any error for free.
public sealed class FlowMapperNodeTests
{
    [Fact]
    public async Task MapsObjectInputToObjectOutputPreservingCorrelationId()
    {
        var engine = new RecordingExpressionEngine(
            evaluate: (_, context, _) => $"{context.Variables["input"]}-mapped");
        await using var node = new FlowMapperNode<object, object>(
            new MapperOptions { Expression = "map", BoundedCapacity = 4 },
            engine);
        var results = Sink(node.Output);
        node.Failed.LinkTo(DataflowBlock.NullTarget<FlowMessage<object>>());

        var message = FlowMessage.Create<object>("value");
        await node.Input.SendAsync(message);

        var result = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.CorrelationId.ShouldBe(message.CorrelationId);   // the whole point of the envelope
        result.Payload.ShouldBe("value-mapped");
    }

    [Fact]
    public async Task UsesSuppliedContextFactoryAndTypes()
    {
        var engine = new RecordingExpressionEngine(evaluate: (_, context, resultType) =>
        {
            resultType.ShouldBe(typeof(OutputMessage));
            return new OutputMessage((int)context.Variables["mapped"]!);
        });
        await using var node = new FlowMapperNode<InputMessage, OutputMessage>(
            new MapperOptions { Expression = "map", InputType = "app.input", OutputType = "app.output" },
            engine,
            contextFactory: new TypedMappingContextFactory<InputMessage>(new InputMessageContextFactory()));
        var results = Sink(node.Output);
        node.Failed.LinkTo(DataflowBlock.NullTarget<FlowMessage<InputMessage>>());

        await node.Input.SendAsync(FlowMessage.Create(new InputMessage(21)));

        var result = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.Payload.Value.ShouldBe(42);
    }

    [Fact]
    public async Task Output_FansOutEveryResultToEveryConsumer()
    {
        // One node's output linked to two downstream consumers, no engine. Both see
        // every mapped result.
        var engine = new RecordingExpressionEngine(
            evaluate: (_, context, _) => $"{context.Variables["input"]}-mapped");
        await using var node = new FlowMapperNode<object, object>(
            new MapperOptions { Expression = "map" },
            engine);
        var logger = Sink(node.Output);
        var mapper = Sink(node.Output);
        node.Failed.LinkTo(DataflowBlock.NullTarget<FlowMessage<object>>());

        await node.Input.SendAsync(FlowMessage.Create<object>("a"));
        await node.Input.SendAsync(FlowMessage.Create<object>("b"));

        (await logger.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.ShouldBe("a-mapped");
        (await logger.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.ShouldBe("b-mapped");
        (await mapper.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.ShouldBe("a-mapped");
        (await mapper.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.ShouldBe("b-mapped");
    }

    [Fact]
    public async Task UsesTheSuppliedExpressionEngine()
    {
        var engine = new RecordingExpressionEngine(
            "named",
            (_, context, _) => $"{context.Variables["input"]}-named");
        await using var node = new FlowMapperNode<object, object>(
            new MapperOptions { Expression = "map" },
            engine);
        var results = Sink(node.Output);
        node.Failed.LinkTo(DataflowBlock.NullTarget<FlowMessage<object>>());

        await node.Input.SendAsync(FlowMessage.Create<object>("value"));

        (await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.ShouldBe("value-named");
    }

    [Fact]
    public async Task FailureReportsErrorWithCorrelationIdAndContinues()
    {
        var calls = 0;
        var engine = new RecordingExpressionEngine(evaluate: (_, context, _) =>
        {
            calls++;
            if (calls == 1)
            {
                throw new InvalidOperationException("bad expression");
            }

            return $"{context.Variables["input"]}-ok";
        });
        await using var node = new FlowMapperNode<object, object>(
            new MapperOptions { Expression = "map", ExpressionName = "test-map" },
            engine);
        var errors = Sink(node.Errors);
        var results = Sink(node.Output);
        node.Failed.LinkTo(DataflowBlock.NullTarget<FlowMessage<object>>());

        var bad = FlowMessage.Create<object>("first");
        await node.Input.SendAsync(bad);
        await node.Input.SendAsync(FlowMessage.Create<object>("second"));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(MappingErrorCodes.MapperFailed);
        error.CorrelationId.ShouldBe(bad.CorrelationId);
        error.Context!.ShouldContain("expressionName=test-map");

        // The pump keeps going: the second message still maps.
        (await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.ShouldBe("second-ok");
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task ReportsExpectedTypeWhenResultIsIncompatible()
    {
        // The compiled-mapper path casts the engine result to the output type, so a
        // wrong-typed return surfaces as a raw InvalidCastException. The node must
        // still report MapperFailed but with a message naming the expected type.
        var engine = new RecordingExpressionEngine(evaluate: (_, _, _) => "not-an-output-message");
        await using var node = new FlowMapperNode<object, OutputMessage>(
            new MapperOptions { Expression = "map", OutputType = "app.output", ExpressionName = "test-map" },
            engine);
        var errors = Sink(node.Errors);
        node.Output.LinkTo(DataflowBlock.NullTarget<FlowMessage<OutputMessage>>());
        node.Failed.LinkTo(DataflowBlock.NullTarget<FlowMessage<object>>());

        await node.Input.SendAsync(FlowMessage.Create<object>("value"));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(MappingErrorCodes.MapperFailed);
        error.Message.ShouldContain("incompatible or null value");
        error.Message.ShouldContain(typeof(OutputMessage).ToString());
        error.Message.ShouldContain("app.output");
        error.Exception.ShouldBeOfType<InvalidCastException>();
    }

    [Fact]
    public async Task ReportsExpectedTypeWhenResultIsNull()
    {
        // A null return for a non-nullable value-type output surfaces as a raw cast
        // failure; the node must report the expected type instead.
        var engine = new RecordingExpressionEngine(evaluate: (_, _, _) => null);
        await using var node = new FlowMapperNode<object, int>(
            new MapperOptions { Expression = "map", OutputType = "app.count", ExpressionName = "test-map" },
            engine);
        var errors = Sink(node.Errors);
        node.Output.LinkTo(DataflowBlock.NullTarget<FlowMessage<int>>());
        node.Failed.LinkTo(DataflowBlock.NullTarget<FlowMessage<object>>());

        await node.Input.SendAsync(FlowMessage.Create<object>("value"));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(MappingErrorCodes.MapperFailed);
        error.Message.ShouldContain("incompatible or null value");
        error.Message.ShouldContain(typeof(int).ToString());
        // A null result for a value-type output surfaces as InvalidCastException or
        // NullReferenceException depending on the cast; both route to the clearer message.
        (error.Exception is InvalidCastException or NullReferenceException).ShouldBeTrue();
    }

    [Fact]
    public async Task FailedPortReceivesDroppedInputCarryingCorrelationId()
    {
        var calls = 0;
        var engine = new RecordingExpressionEngine(evaluate: (_, context, _) =>
        {
            calls++;
            if (calls == 1)
            {
                throw new InvalidOperationException("bad expression");
            }

            return $"{context.Variables["input"]}-ok";
        });
        await using var node = new FlowMapperNode<object, object>(
            new MapperOptions { Expression = "map", ExpressionName = "test-map" },
            engine);
        var errors = Sink(node.Errors);
        var failed = Sink(node.Failed);
        var results = Sink(node.Output);

        var bad = FlowMessage.Create<object>("first");
        await node.Input.SendAsync(bad);
        await node.Input.SendAsync(FlowMessage.Create<object>("second"));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(MappingErrorCodes.MapperFailed);
        error.Context!.ShouldContain("expressionName=test-map");

        var dropped = await failed.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        dropped.CorrelationId.ShouldBe(bad.CorrelationId);
        dropped.Payload.ShouldBe("first");

        (await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.ShouldBe("second-ok");
    }

    [Fact]
    public async Task EmitsSuccessEventCarryingCorrelationIdAndAttributes()
    {
        var engine = new RecordingExpressionEngine(evaluate: (_, context, _) => context.Variables["input"]);
        await using var node = new FlowMapperNode<object, object>(
            new MapperOptions { Expression = "map", ExpressionId = "copy-v1", ExpressionName = "copy" },
            engine);
        var events = Sink(node.Events);
        node.Output.LinkTo(DataflowBlock.NullTarget<FlowMessage<object>>());
        node.Failed.LinkTo(DataflowBlock.NullTarget<FlowMessage<object>>());

        var message = FlowMessage.Create<object>("value");
        await node.Input.SendAsync(message);

        var @event = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        @event.Name.ShouldBe(FlowMapperNode<object, object>.MapperSucceeded);
        @event.Level.ShouldBe(FlowEventLevel.Information);
        @event.CorrelationId.ShouldBe(message.CorrelationId);
        @event.Attributes["inputType"].ShouldBe("object");
        @event.Attributes["outputType"].ShouldBe("object");
        @event.Attributes["engine"].ShouldBe("test");
        @event.Attributes["expressionId"].ShouldBe("copy-v1");
        @event.Attributes["expressionName"].ShouldBe("copy");
    }

    [Fact]
    public async Task ConfiguredClock_StampsEventTimestamp()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-02T13:00:00Z");
        var engine = new RecordingExpressionEngine(evaluate: (_, context, _) => context.Variables["input"]);
        await using var node = new FlowMapperNode<object, object>(
            new MapperOptions { Expression = "map" },
            engine,
            clock: new FakeTimeProvider(timestamp));
        var events = Sink(node.Events);
        node.Output.LinkTo(DataflowBlock.NullTarget<FlowMessage<object>>());
        node.Failed.LinkTo(DataflowBlock.NullTarget<FlowMessage<object>>());

        await node.Input.SendAsync(FlowMessage.Create<object>("value"));

        (await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Timestamp.ShouldBe(timestamp);
    }

    [Fact]
    public void Constructor_RejectsMissingExpression()
    {
        var exception = Should.Throw<ArgumentException>(
            () => new FlowMapperNode<object, object>(
                new MapperOptions { Expression = null },
                new RecordingExpressionEngine()));

        exception.Message.ShouldContain("expression");
    }

    [Fact]
    public void Constructor_RequiresOptions()
        => Should.Throw<ArgumentNullException>(
            () => new FlowMapperNode<object, object>(null!, new RecordingExpressionEngine()));

    [Fact]
    public void Constructor_RequiresExpressionEngine()
        => Should.Throw<ArgumentNullException>(
            () => new FlowMapperNode<object, object>(new MapperOptions { Expression = "map" }, null!));

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = false });
        return sink;
    }

    private sealed record InputMessage(int Value);

    private sealed record OutputMessage(int Value);

    private sealed class InputMessageContextFactory : IFlowMapContextFactory<InputMessage>
    {
        public FlowMapContext Create(InputMessage input)
            => new()
            {
                Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["input"] = input,
                    ["value"] = input,
                    ["mapped"] = input.Value * 2
                }
            };
    }

    private sealed class RecordingExpressionEngine(
        string name = "test",
        Func<string, FlowMapContext, Type, object?>? evaluate = null)
        : IFlowExpressionEngine
    {
        public string Name => name;

        public object? Evaluate(string expression, FlowMapContext context, Type resultType)
            => evaluate?.Invoke(expression, context, resultType) ?? context.Variables["input"];
    }
}
