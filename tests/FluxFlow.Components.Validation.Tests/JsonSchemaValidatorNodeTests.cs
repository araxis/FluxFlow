using FluxFlow.Components.Validation;
using FluxFlow.Components.Validation.Contracts;
using FluxFlow.Components.Validation.Nodes;
using FluxFlow.Components.Validation.Options;
using FluxFlow.Nodes;
using Json.Schema;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Validation.Tests;

// Every test news the node directly — no engine, no registry. Messages travel as
// FlowMessage<T> envelopes; the correlation id flows input -> result/Valid/Invalid
// and onto any error for free.
public sealed class JsonSchemaValidatorNodeTests
{
    [Fact]
    public async Task ValidInput_RoutesToValidAndResultPreservingCorrelationId()
    {
        await using var node = new JsonSchemaValidatorNode<JsonElement>(OrderSchema());
        var results = Sink(node.Output);
        var valid = Sink(node.Valid);
        node.Invalid.LinkTo(DataflowBlock.NullTarget<FlowMessage<JsonElement>>());

        var order = JsonSerializer.SerializeToElement(new { id = "A-100", total = 125 });
        var message = FlowMessage.Create(order);
        await node.Input.SendAsync(message);

        var result = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.CorrelationId.ShouldBe(message.CorrelationId);
        result.Payload.IsValid.ShouldBeTrue();
        result.Payload.Issues.ShouldBeEmpty();
        result.Payload.ValueSelector.ShouldBe("input");

        var routed = await valid.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        routed.CorrelationId.ShouldBe(message.CorrelationId);
        routed.Payload.GetProperty("id").GetString().ShouldBe("A-100");
    }

    [Fact]
    public async Task Output_FansOutEveryResultToEveryConsumer()
    {
        // One node's output linked to two downstream consumers, no engine. Both see
        // every result.
        await using var node = new JsonSchemaValidatorNode<JsonElement>(OrderSchema());
        var logger = Sink(node.Output);
        var mapper = Sink(node.Output);
        node.Valid.LinkTo(DataflowBlock.NullTarget<FlowMessage<JsonElement>>());
        node.Invalid.LinkTo(DataflowBlock.NullTarget<FlowMessage<JsonElement>>());

        await node.Input.SendAsync(FlowMessage.Create(
            JsonSerializer.SerializeToElement(new { id = "A-1", total = 1 })));
        await node.Input.SendAsync(FlowMessage.Create(
            JsonSerializer.SerializeToElement(new { id = "A-2", total = 2 })));

        (await logger.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.Input
            .GetProperty("id").GetString().ShouldBe("A-1");
        (await logger.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.Input
            .GetProperty("id").GetString().ShouldBe("A-2");
        (await mapper.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.Input
            .GetProperty("id").GetString().ShouldBe("A-1");
        (await mapper.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.Input
            .GetProperty("id").GetString().ShouldBe("A-2");
    }

    [Fact]
    public async Task ConfiguredClock_SetsResultTimestamp()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-02T13:00:00Z");
        await using var node = new JsonSchemaValidatorNode<JsonElement>(
            OrderSchema(),
            clock: new FakeTimeProvider(timestamp));
        var results = Sink(node.Output);
        node.Valid.LinkTo(DataflowBlock.NullTarget<FlowMessage<JsonElement>>());
        node.Invalid.LinkTo(DataflowBlock.NullTarget<FlowMessage<JsonElement>>());

        await node.Input.SendAsync(FlowMessage.Create(
            JsonSerializer.SerializeToElement(new { id = "A-100", total = 125 })));

        var result = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.Payload.Timestamp.ShouldBe(timestamp);
    }

    [Fact]
    public async Task InvalidInput_RoutesToInvalidWithoutEmittingFlowError()
    {
        await using var node = new JsonSchemaValidatorNode<JsonElement>(OrderSchema());
        var results = Sink(node.Output);
        var invalid = Sink(node.Invalid);
        var errors = Sink(node.Errors);
        node.Valid.LinkTo(DataflowBlock.NullTarget<FlowMessage<JsonElement>>());

        var order = JsonSerializer.SerializeToElement(new { id = "A-100", total = "wrong" });
        var message = FlowMessage.Create(order);
        await node.Input.SendAsync(message);

        var result = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.Payload.IsValid.ShouldBeFalse();
        result.Payload.Issues.ShouldNotBeEmpty();

        var routed = await invalid.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        routed.CorrelationId.ShouldBe(message.CorrelationId);
        routed.Payload.GetProperty("id").GetString().ShouldBe("A-100");

        errors.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task SchemaPath_LoadsSchemaFromFile()
    {
        var schemaPath = Path.Combine(Path.GetTempPath(), $"fluxflow-schema-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(schemaPath, OrderSchemaJson().GetRawText());
        try
        {
            var schema = new JsonSchemaValidatorOptions { SchemaPath = schemaPath }.LoadSchema();
            await using var node = new JsonSchemaValidatorNode<string>(schema, schemaPath: schemaPath);
            var valid = Sink(node.Valid);
            node.Output.LinkTo(DataflowBlock.NullTarget<FlowMessage<JsonSchemaValidationResult<string>>>());
            node.Invalid.LinkTo(DataflowBlock.NullTarget<FlowMessage<string>>());

            await node.Input.SendAsync(FlowMessage.Create("""{"id":"A-100","total":125}"""));

            (await valid.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.ShouldContain("A-100");
        }
        finally
        {
            File.Delete(schemaPath);
        }
    }

    [Fact]
    public void LoadSchema_FailsWhenSchemaMissing()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => new JsonSchemaValidatorOptions { InputType = "object" }.LoadSchema());

        exception.Message.ShouldContain("schema");
    }

    [Fact]
    public void LoadSchema_FailsWhenSchemaMalformed()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => new JsonSchemaValidatorOptions
            {
                Schema = JsonSerializer.SerializeToElement("{")
            }.LoadSchema());

        exception.Message.ShouldContain("schema");
    }

    [Fact]
    public async Task SelectorFailure_ReportsErrorWithCorrelationIdAndContinues()
    {
        var calls = 0;
        var selector = new DelegateSelector<InputMessage>((message, _) =>
        {
            calls++;
            if (calls == 1)
            {
                throw new InvalidOperationException("selector failed");
            }

            return message.Payload;
        });
        await using var node = new JsonSchemaValidatorNode<InputMessage>(
            OrderSchema(),
            selector: selector,
            valueSelector: "payload");
        var errors = Sink(node.Errors);
        var valid = Sink(node.Valid);
        node.Output.LinkTo(DataflowBlock.NullTarget<FlowMessage<JsonSchemaValidationResult<InputMessage>>>());
        node.Invalid.LinkTo(DataflowBlock.NullTarget<FlowMessage<InputMessage>>());

        var bad = FlowMessage.Create(new InputMessage("""{"id":"bad","total":1}"""));
        await node.Input.SendAsync(bad);
        await node.Input.SendAsync(FlowMessage.Create(new InputMessage("""{"id":"A-100","total":125}""")));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(ValidationErrorCodes.ValueSelectorFailed);
        error.CorrelationId.ShouldBe(bad.CorrelationId);

        // The pump keeps going: the second (well-formed) message still validates.
        (await valid.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.Payload.ShouldContain("A-100");
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task ConvertsBytes()
    {
        await using var node = new JsonSchemaValidatorNode<byte[]>(OrderSchema());
        var valid = Sink(node.Valid);
        node.Output.LinkTo(DataflowBlock.NullTarget<FlowMessage<JsonSchemaValidationResult<byte[]>>>());
        node.Invalid.LinkTo(DataflowBlock.NullTarget<FlowMessage<byte[]>>());

        await node.Input.SendAsync(FlowMessage.Create(
            Encoding.UTF8.GetBytes("""{"id":"A-100","total":125}""")));

        (await valid.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.Length.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task ConvertsJsonNodes()
    {
        await using var node = new JsonSchemaValidatorNode<JsonNode>(OrderSchema());
        var valid = Sink(node.Valid);
        node.Output.LinkTo(DataflowBlock.NullTarget<FlowMessage<JsonSchemaValidationResult<JsonNode>>>());
        node.Invalid.LinkTo(DataflowBlock.NullTarget<FlowMessage<JsonNode>>());

        await node.Input.SendAsync(FlowMessage.Create(
            JsonNode.Parse("""{"id":"A-100","total":125}""")!));

        (await valid.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5)))
            .Payload["id"]!.GetValue<string>().ShouldBe("A-100");
    }

    [Fact]
    public async Task ConvertsPlainObjects()
    {
        await using var node = new JsonSchemaValidatorNode<InputObject>(OrderSchema());
        var valid = Sink(node.Valid);
        node.Output.LinkTo(DataflowBlock.NullTarget<FlowMessage<JsonSchemaValidationResult<InputObject>>>());
        node.Invalid.LinkTo(DataflowBlock.NullTarget<FlowMessage<InputObject>>());

        await node.Input.SendAsync(FlowMessage.Create(new InputObject("A-100", 125)));

        (await valid.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.Id.ShouldBe("A-100");
    }

    [Fact]
    public async Task Completion_PropagatesToOutputSinks()
    {
        var node = new JsonSchemaValidatorNode<JsonElement>(OrderSchema());
        // Propagate completion here so the sink observes the broadcast finishing.
        var results = new BufferBlock<FlowMessage<JsonSchemaValidationResult<JsonElement>>>();
        node.Output.LinkTo(results, new DataflowLinkOptions { PropagateCompletion = true });
        node.Valid.LinkTo(DataflowBlock.NullTarget<FlowMessage<JsonElement>>());
        node.Invalid.LinkTo(DataflowBlock.NullTarget<FlowMessage<JsonElement>>());

        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        // Completion propagates through the broadcast output asynchronously.
        await results.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task EmitsLoadedAndValidEvents()
    {
        await using var node = new JsonSchemaValidatorNode<JsonElement>(OrderSchema(), schemaId: "orders");
        var events = Sink(node.Events);
        node.Output.LinkTo(DataflowBlock.NullTarget<FlowMessage<JsonSchemaValidationResult<JsonElement>>>());
        node.Valid.LinkTo(DataflowBlock.NullTarget<FlowMessage<JsonElement>>());
        node.Invalid.LinkTo(DataflowBlock.NullTarget<FlowMessage<JsonElement>>());

        var message = FlowMessage.Create(JsonSerializer.SerializeToElement(new { id = "A-100", total = 125 }));
        await node.Input.SendAsync(message);

        var loaded = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        loaded.Name.ShouldBe(JsonSchemaValidatorNode<JsonElement>.SchemaLoaded);

        var valid = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        valid.Name.ShouldBe(JsonSchemaValidatorNode<JsonElement>.SchemaValid);
        valid.CorrelationId.ShouldBe(message.CorrelationId);
        valid.Attributes["schemaId"].ShouldBe("orders");
    }

    [Fact]
    public void Constructor_RequiresSchema()
        => Should.Throw<ArgumentNullException>(() => new JsonSchemaValidatorNode<JsonElement>(null!));

    [Fact]
    public void Constructor_RejectsInvalidBoundedCapacity()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new JsonSchemaValidatorNode<JsonElement>(
                OrderSchema(),
                options: new JsonSchemaValidatorOptions { BoundedCapacity = 0 }))
            .Message.ShouldContain("boundedCapacity");

    [Fact]
    public void Constructor_RejectsBlankInputType()
        => Should.Throw<ArgumentException>(
            () => new JsonSchemaValidatorNode<JsonElement>(
                OrderSchema(),
                options: new JsonSchemaValidatorOptions { InputType = " " }))
            .Message.ShouldContain("inputType");

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = false });
        return sink;
    }

    private static JsonSchema OrderSchema()
        => new JsonSchemaValidatorOptions { Schema = OrderSchemaJson() }.LoadSchema();

    private static JsonElement OrderSchemaJson()
        => JsonSerializer.SerializeToElement(new
        {
            type = "object",
            required = new[] { "id", "total" },
            properties = new
            {
                id = new { type = "string" },
                total = new { type = "number" }
            }
        });

    private sealed class DelegateSelector<TInput>(
        Func<TInput, JsonSchemaValidatorContext, object?> selector)
        : IJsonSchemaValueSelector<TInput>
    {
        public object? Select(TInput input, JsonSchemaValidatorContext context)
            => selector(input, context);
    }

    private sealed record InputMessage(string Payload);

    private sealed record InputObject(string Id, decimal Total);
}
