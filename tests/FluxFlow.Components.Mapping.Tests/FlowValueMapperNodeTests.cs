using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Mapping.Contracts;
using FluxFlow.Components.Mapping.Nodes;
using FluxFlow.Components.Mapping.Options;
using FluxFlow.Data;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mapping.Tests;

public sealed class FlowValueMapperNodeTests
{
    [Fact]
    public async Task Maps_flow_values_without_serialization_and_preserves_message_identity()
    {
        var input = FlowValue.FromObject(new Dictionary<string, FlowValue>
        {
            ["count"] = FlowValue.From(2),
            ["name"] = FlowValue.From("sample")
        });
        var mapped = FlowValue.FromObject(new Dictionary<string, FlowValue>
        {
            ["count"] = FlowValue.From(4),
            ["name"] = input.GetObject()["name"]
        });
        var engine = new RecordingExpressionEngine((_, context, resultType) =>
        {
            resultType.ShouldBe(typeof(FlowValue));
            context.Variables["input"].ShouldBeSameAs(input);
            context.Variables["value"].ShouldBeSameAs(input);
            return mapped;
        });
        await using var node = new FlowValueMapperNode(
            new MapperOptions { Expression = "map", BoundedCapacity = 4 },
            engine);
        var results = Sink(node.Output);
        var request = FlowMessage.Create(input);

        (await node.Input.SendAsync(request)).ShouldBeTrue();

        var response = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        response.Payload.Kind.ShouldBe(MappingResultKinds.Mapped);
        response.Payload.IsError.ShouldBeFalse();
        response.Payload.Value.ShouldBeSameAs(mapped);
        response.CorrelationId.ShouldBe(request.CorrelationId);
        response.TraceId.ShouldBe(request.TraceId);
        response.CausationId.ShouldBe(request.MessageId);
        response.MessageId.ShouldNotBe(request.MessageId);
    }

    [Fact]
    public async Task Output_fans_out_every_accepted_result_in_order()
    {
        var engine = new RecordingExpressionEngine(
            (_, context, _) => context.Variables["input"]);
        await using var node = new FlowValueMapperNode(
            new MapperOptions { Expression = "map" },
            engine);
        var first = Sink(node.Output);
        var second = Sink(node.Output);
        var values = new[] { FlowValue.From("a"), FlowValue.From("b") };

        foreach (var value in values)
            (await node.Input.SendAsync(FlowMessage.Create(value))).ShouldBeTrue();

        foreach (var sink in new[] { first, second })
        {
            (await sink.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5)))
                .Payload.Value.ShouldBeSameAs(values[0]);
            (await sink.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5)))
                .Payload.Value.ShouldBeSameAs(values[1]);
        }
    }

    [Fact]
    public async Task Emits_expected_failures_as_results_and_continues_processing()
    {
        var timestamp = DateTimeOffset.Parse("2026-07-18T10:00:00Z");
        var calls = 0;
        var engine = new RecordingExpressionEngine((_, context, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                throw new InvalidOperationException("invalid value");
            return context.Variables["input"];
        });
        await using var node = new FlowValueMapperNode(
            new MapperOptions
            {
                Expression = "map",
                ExpressionName = "normalize",
                InputType = "app.input",
                OutputType = "app.output"
            },
            engine,
            clock: new FakeTimeProvider(timestamp));
        var results = Sink(node.Output);
        var events = Sink(node.Events);
        var invalid = FlowValue.From("invalid");
        var valid = FlowValue.From("valid");

        await node.Input.SendAsync(FlowMessage.Create(invalid));
        await node.Input.SendAsync(FlowMessage.Create(valid));

        var failure = (await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload;
        failure.Kind.ShouldBe(MappingResultKinds.Failed);
        failure.IsError.ShouldBeTrue();
        failure.Value.ShouldBeSameAs(invalid);
        failure.Timestamp.ShouldBe(timestamp);
        failure.Error!.Code.ShouldBe(MappingErrorCodeNames.MapperFailed);
        failure.Error.Category.ShouldBe("Mapping");
        failure.Error.IsTransient.ShouldBeFalse();
        failure.Error.Details.GetObject()["expressionName"].GetString()
            .ShouldBe("normalize");

        var success = (await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload;
        success.IsError.ShouldBeFalse();
        success.Value.ShouldBeSameAs(valid);

        var failedEvent = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        failedEvent.Name.ShouldBe(FlowValueMapperNode.MapperFailed);
        failedEvent.Level.ShouldBe(FlowEventLevel.Warning);
        var succeededEvent = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        succeededEvent.Name.ShouldBe(FlowValueMapperNode.MapperSucceeded);

        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        node.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task Uses_optional_context_factory_with_flow_value_types()
    {
        var contextFactory = new RecordingContextFactory();
        var engine = new RecordingExpressionEngine((_, context, _) =>
            context.Variables["mapped"]);
        await using var node = new FlowValueMapperNode(
            new MapperOptions { Expression = "map" },
            engine,
            contextFactory);
        var results = Sink(node.Output);
        var input = FlowValue.From("input");

        await node.Input.SendAsync(FlowMessage.Create(input));

        var result = (await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload;
        result.Value!.GetString().ShouldBe("mapped");
        contextFactory.Input.ShouldBeSameAs(input);
        contextFactory.Context!.InputType.ShouldBe(typeof(FlowValue));
        contextFactory.Context.OutputType.ShouldBe(typeof(FlowValue));
    }

    [Fact]
    public async Task Incompatible_expression_output_is_a_normal_failure_result()
    {
        var engine = new RecordingExpressionEngine((_, _, _) => "not-a-flow-value");
        await using var node = new FlowValueMapperNode(
            new MapperOptions
            {
                Expression = "map",
                InputType = "app.input",
                OutputType = "app.output"
            },
            engine);
        var results = Sink(node.Output);
        var input = FlowValue.From("input");

        (await node.Input.SendAsync(FlowMessage.Create(input))).ShouldBeTrue();

        var result = (await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload;
        result.Kind.ShouldBe(MappingResultKinds.Failed);
        result.IsError.ShouldBeTrue();
        result.Value.ShouldBeSameAs(input);
        result.Error.ShouldNotBeNull().Code.ShouldBe(MappingErrorCodeNames.MapperFailed);
        var details = result.Error.Details.GetObject();
        details["inputType"].GetString().ShouldBe("app.input");
        details["outputType"].GetString().ShouldBe("app.output");
        details["exceptionType"].GetString().ShouldContain(nameof(InvalidCastException));
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Success_event_uses_configured_clock_and_diagnostic_metadata()
    {
        var timestamp = DateTimeOffset.Parse("2026-07-23T08:00:00Z");
        var engine = new RecordingExpressionEngine(
            (_, context, _) => context.Variables["input"]);
        await using var node = new FlowValueMapperNode(
            new MapperOptions
            {
                Expression = "map",
                ExpressionId = "map-v1",
                ExpressionName = "normalize",
                InputType = "app.input",
                OutputType = "app.output"
            },
            engine,
            clock: new FakeTimeProvider(timestamp));
        var events = Sink(node.Events);
        var message = FlowMessage.Create(FlowValue.From("input"));

        (await node.Input.SendAsync(message)).ShouldBeTrue();

        var @event = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        @event.Timestamp.ShouldBe(timestamp);
        @event.CorrelationId.ShouldBe(message.CorrelationId);
        @event.Name.ShouldBe(FlowValueMapperNode.MapperSucceeded);
        @event.Attributes["engine"].ShouldBe("test");
        @event.Attributes["expressionId"].ShouldBe("map-v1");
        @event.Attributes["expressionName"].ShouldBe("normalize");
        @event.Attributes["inputType"].ShouldBe("app.input");
        @event.Attributes["outputType"].ShouldBe("app.output");
    }

    [Fact]
    public void Constructor_validates_options_and_engine()
    {
        Should.Throw<ArgumentNullException>(() =>
            new FlowValueMapperNode(null!, new RecordingExpressionEngine()));
        Should.Throw<ArgumentNullException>(() =>
            new FlowValueMapperNode(
                new MapperOptions { Expression = "map" },
                null!));
        Should.Throw<ArgumentException>(() =>
            new FlowValueMapperNode(
                new MapperOptions(),
                new RecordingExpressionEngine()));
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new FlowValueMapperNode(
                new MapperOptions { Expression = "map", BoundedCapacity = 0 },
                new RecordingExpressionEngine()));
    }

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink);
        return sink;
    }

    private sealed class RecordingContextFactory : IMappingContextFactory
    {
        public object? Input { get; private set; }

        public MappingNodeContext? Context { get; private set; }

        public FlowMapContext Create(object? input, MappingNodeContext context)
        {
            Input = input;
            Context = context;
            return new FlowMapContext
            {
                Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["input"] = input,
                    ["mapped"] = FlowValue.From("mapped")
                }
            };
        }
    }

    private sealed class RecordingExpressionEngine(
        Func<string, FlowMapContext, Type, object?>? evaluate = null)
        : IFlowExpressionEngine
    {
        public string Name => "test";

        public object? Evaluate(
            string expression,
            FlowMapContext context,
            Type resultType)
            => evaluate?.Invoke(expression, context, resultType)
               ?? context.Variables["input"];
    }
}
