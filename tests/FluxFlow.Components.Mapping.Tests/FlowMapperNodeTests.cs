using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Mapping.Contracts;
using FluxFlow.Components.Mapping.Nodes;
using FluxFlow.Components.Mapping.Options;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mapping.Tests;

public sealed class FlowMapperNodeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Maps_typed_values_and_preserves_message_lineage()
    {
        var input = new InputModel(2, "sample");
        var mapped = new OutputModel(4, input.Name);
        var engine = new RecordingExpressionEngine((_, context, resultType) =>
        {
            resultType.ShouldBe(typeof(OutputModel));
            context.Variables["input"].ShouldBeSameAs(input);
            context.Variables["value"].ShouldBeSameAs(input);
            return mapped;
        });
        await using var node = new FlowMapperNode<InputModel, OutputModel>(
            new MapperOptions { Expression = "map", BoundedCapacity = 4 },
            engine);
        var results = Sink(node.Output);
        var request = FlowMessage.Create(input);

        (await node.Input.SendAsync(request)).ShouldBeTrue();

        var response = await results.ReceiveAsync().WaitAsync(Timeout);
        response.IsError.ShouldBeFalse();
        response.Value.ShouldBeSameAs(mapped);
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
        await using var node = new FlowMapperNode<string, string>(
            new MapperOptions { Expression = "map" },
            engine);
        var first = Sink(node.Output);
        var second = Sink(node.Output);
        string[] values = ["a", "b"];

        foreach (var value in values)
            (await node.Input.SendAsync(FlowMessage.Create(value))).ShouldBeTrue();

        foreach (var sink in new[] { first, second })
        {
            (await sink.ReceiveAsync().WaitAsync(Timeout)).Value.ShouldBe(values[0]);
            (await sink.ReceiveAsync().WaitAsync(Timeout)).Value.ShouldBe(values[1]);
        }
    }

    [Fact]
    public async Task Output_delivers_all_ordered_results_to_two_subscribers_under_backpressure()
    {
        var engine = new RecordingExpressionEngine(
            (_, context, _) => context.Variables["input"]);
        await using var node = new FlowMapperNode<string, string>(
            new MapperOptions { Expression = "map", BoundedCapacity = 1 },
            engine);
        var fast = Sink(node.Output);
        var slow = new PostponedTargetBlock<FlowMessage<string>>();
        using var slowLink = node.Output.LinkTo(
            slow,
            new DataflowLinkOptions { PropagateCompletion = true });
        var events = Sink(node.Events);
        var inputs = Enumerable.Range(1, 4)
            .Select(index => FlowMessage.Create($"value-{index}"))
            .ToArray();

        (await node.Input.SendAsync(inputs[0]).WaitAsync(Timeout)).ShouldBeTrue();
        await slow.WaitForOfferAsync(Timeout);
        (await node.Input.SendAsync(inputs[1]).WaitAsync(Timeout)).ShouldBeTrue();
        await ReceiveEventsAsync(events, 2);
        (await node.Input.SendAsync(inputs[2]).WaitAsync(Timeout)).ShouldBeTrue();
        await ReceiveEventAsync(events, inputs[2].CorrelationId);

        var fourthInput = node.Input.SendAsync(inputs[3]);
        fourthInput.IsCompleted.ShouldBeFalse();

        slow.AcceptNext();
        (await fourthInput.WaitAsync(Timeout)).ShouldBeTrue();
        node.Complete();
        node.Completion.IsCompleted.ShouldBeFalse();

        for (var index = 1; index < inputs.Length; index++)
        {
            await slow.WaitForOfferAsync(Timeout);
            slow.AcceptNext();
        }

        await node.Completion.WaitAsync(Timeout);
        await slow.Completion.WaitAsync(Timeout);
        var fastMessages = await ReceiveAsync(fast, inputs.Length);
        var slowMessages = slow.Accepted;

        fastMessages.Select(static message => message.Value)
            .ShouldBe(["value-1", "value-2", "value-3", "value-4"]);
        slowMessages.Select(static message => message.Value)
            .ShouldBe(["value-1", "value-2", "value-3", "value-4"]);
        fastMessages.Select(static message => message.CorrelationId)
            .ShouldBe(inputs.Select(static message => message.CorrelationId));
        slowMessages.Select(static message => message.CorrelationId)
            .ShouldBe(inputs.Select(static message => message.CorrelationId));
        fastMessages.Select(static message => message.CausationId)
            .ShouldBe(inputs.Select(static message => (MessageId?)message.MessageId));
        slowMessages.Select(static message => message.CausationId)
            .ShouldBe(inputs.Select(static message => (MessageId?)message.MessageId));
    }

    [Fact]
    public async Task Expected_failure_is_an_error_message_and_processing_continues()
    {
        var timestamp = DateTimeOffset.Parse("2026-07-18T10:00:00Z");
        var calls = 0;
        var engine = new RecordingExpressionEngine((_, context, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                throw new InvalidOperationException("invalid value");
            return context.Variables["input"];
        });
        await using var node = new FlowMapperNode<string, string>(
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

        await node.Input.SendAsync(FlowMessage.Create("invalid"));
        await node.Input.SendAsync(FlowMessage.Create("valid"));

        var failure = await results.ReceiveAsync().WaitAsync(Timeout);
        failure.IsError.ShouldBeTrue();
        failure.Error.ShouldNotBeNull().Code.ShouldBe(MappingErrorCodeNames.MapperFailed);
        failure.Error.Category.ShouldBe("Mapping");
        failure.Error.IsTransient.ShouldBeFalse();
        failure.Error.Details!.Value.GetProperty("expressionName").GetString()
            .ShouldBe("normalize");

        var success = await results.ReceiveAsync().WaitAsync(Timeout);
        success.IsError.ShouldBeFalse();
        success.Value.ShouldBe("valid");

        var failedEvent = await events.ReceiveAsync().WaitAsync(Timeout);
        failedEvent.Name.ShouldBe(FlowMapperNode<string, string>.MapperFailed);
        failedEvent.Level.ShouldBe(FlowEventLevel.Warning);
        var succeededEvent = await events.ReceiveAsync().WaitAsync(Timeout);
        succeededEvent.Name.ShouldBe(FlowMapperNode<string, string>.MapperSucceeded);

        node.Complete();
        await node.Completion.WaitAsync(Timeout);
        node.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task Uses_optional_context_factory_with_typed_contracts()
    {
        var contextFactory = new RecordingContextFactory();
        var engine = new RecordingExpressionEngine((_, context, _) =>
            context.Variables["mapped"]);
        await using var node = new FlowMapperNode<string, string>(
            new MapperOptions { Expression = "map" },
            engine,
            contextFactory);
        var results = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create("input"));

        var result = await results.ReceiveAsync().WaitAsync(Timeout);
        result.Value.ShouldBe("mapped");
        contextFactory.Input.ShouldBe("input");
        contextFactory.Context!.InputType.ShouldBe(typeof(string));
        contextFactory.Context.OutputType.ShouldBe(typeof(string));
    }

    [Fact]
    public async Task Incompatible_expression_output_is_a_normal_error_message()
    {
        var engine = new RecordingExpressionEngine((_, _, _) => "not-json");
        await using var node = new JsonMapperNode(
            new MapperOptions
            {
                Expression = "map",
                InputType = "app.input",
                OutputType = "app.output"
            },
            engine);
        var results = Sink(node.Output);

        (await node.Input.SendAsync(FlowMessage.Create(
            JsonSerializer.SerializeToElement(new { value = "input" })))).ShouldBeTrue();

        var result = await results.ReceiveAsync().WaitAsync(Timeout);
        result.IsError.ShouldBeTrue();
        result.Error.ShouldNotBeNull().Code.ShouldBe(MappingErrorCodeNames.MapperFailed);
        var details = result.Error.Details!.Value;
        details.GetProperty("inputType").GetString().ShouldBe(typeof(JsonElement).FullName);
        details.GetProperty("outputType").GetString().ShouldBe(typeof(JsonElement).FullName);
        details.GetProperty("exceptionType").GetString()!.ShouldContain(nameof(InvalidCastException));
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Success_event_uses_configured_clock_and_diagnostic_metadata()
    {
        var timestamp = DateTimeOffset.Parse("2026-07-23T08:00:00Z");
        var engine = new RecordingExpressionEngine(
            (_, context, _) => context.Variables["input"]);
        await using var node = new FlowMapperNode<string, string>(
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
        var message = FlowMessage.Create("input");

        (await node.Input.SendAsync(message)).ShouldBeTrue();

        var @event = await events.ReceiveAsync().WaitAsync(Timeout);
        @event.Timestamp.ShouldBe(timestamp);
        @event.CorrelationId.ShouldBe(message.CorrelationId);
        @event.Name.ShouldBe(FlowMapperNode<string, string>.MapperSucceeded);
        @event.Attributes["engine"].ShouldBe("test");
        @event.Attributes["expressionId"].ShouldBe("map-v1");
        @event.Attributes["expressionName"].ShouldBe("normalize");
        @event.Attributes["inputType"].ShouldBe(typeof(string).FullName);
        @event.Attributes["outputType"].ShouldBe(typeof(string).FullName);
    }

    [Fact]
    public void Constructor_validates_options_and_engine()
    {
        Should.Throw<ArgumentNullException>(() =>
            new FlowMapperNode<string, string>(null!, new RecordingExpressionEngine()));
        Should.Throw<ArgumentNullException>(() =>
            new FlowMapperNode<string, string>(
                new MapperOptions { Expression = "map" },
                null!));
        Should.Throw<ArgumentException>(() =>
            new FlowMapperNode<string, string>(
                new MapperOptions(),
                new RecordingExpressionEngine()));
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new FlowMapperNode<string, string>(
                new MapperOptions { Expression = "map", BoundedCapacity = 0 },
                new RecordingExpressionEngine()));
    }

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink);
        return sink;
    }

    private static async Task<IReadOnlyList<T>> ReceiveAsync<T>(BufferBlock<T> sink, int count)
    {
        var items = new List<T>(count);
        for (var index = 0; index < count; index++)
        {
            items.Add(await sink.ReceiveAsync().WaitAsync(Timeout));
        }

        return items;
    }

    private static async Task ReceiveEventsAsync(BufferBlock<FlowEvent> events, int count)
    {
        for (var index = 0; index < count; index++)
        {
            await events.ReceiveAsync().WaitAsync(Timeout);
        }
    }

    private static async Task ReceiveEventAsync(
        BufferBlock<FlowEvent> events,
        CorrelationId? correlationId)
    {
        while (true)
        {
            var @event = await events.ReceiveAsync().WaitAsync(Timeout);
            if (@event.CorrelationId == correlationId)
            {
                return;
            }
        }
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
                    ["mapped"] = "mapped"
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

    private sealed record InputModel(int Count, string Name);

    private sealed record OutputModel(int Count, string Name);
}
