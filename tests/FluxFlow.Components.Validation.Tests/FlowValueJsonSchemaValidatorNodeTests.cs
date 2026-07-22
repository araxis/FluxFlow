using System.Threading.Tasks.Dataflow;
using System.Text.Json;
using FluxFlow.Components.Validation.Contracts;
using FluxFlow.Components.Validation.Diagnostics;
using FluxFlow.Components.Validation.Nodes;
using FluxFlow.Components.Validation.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Json.Schema;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Validation.Tests;

public sealed class FlowValueJsonSchemaValidatorNodeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Valid_and_invalid_values_are_normal_result_variants()
    {
        await using var node = new FlowValueJsonSchemaValidatorNode(OrderSchema());
        var output = Sink(node.Output);
        var validValue = Order("A-100", FlowValue.From(125L));
        var invalidValue = Order("A-101", FlowValue.From("wrong"));
        var validInput = FlowMessage.Create(
            validValue,
            new CorrelationId("valid-order"));
        var invalidInput = FlowMessage.Create(
            invalidValue,
            new CorrelationId("invalid-order"));

        await node.Input.SendAsync(validInput);
        await node.Input.SendAsync(invalidInput);

        var valid = await output.ReceiveAsync().WaitAsync(Timeout);
        var invalid = await output.ReceiveAsync().WaitAsync(Timeout);
        valid.Payload.Kind.ShouldBe(ValidationResultKinds.Valid);
        valid.Payload.IsError.ShouldBeFalse();
        valid.Payload.Value.ShouldNotBeNull().IsValid.ShouldBeTrue();
        valid.Payload.Value.Input.ShouldBeSameAs(validValue);
        valid.Payload.Value.Value.ShouldBeSameAs(validValue);
        valid.CorrelationId.ShouldBe(validInput.CorrelationId);
        valid.TraceId.ShouldBe(validInput.TraceId);
        valid.CausationId.ShouldBe(validInput.MessageId);
        invalid.Payload.Kind.ShouldBe(ValidationResultKinds.Invalid);
        invalid.Payload.IsError.ShouldBeFalse();
        invalid.Payload.Value.ShouldNotBeNull().IsValid.ShouldBeFalse();
        invalid.Payload.Value.Issues.ShouldNotBeEmpty();
        invalid.CorrelationId.ShouldBe(invalidInput.CorrelationId);
    }

    [Fact]
    public async Task Flow_value_selector_returns_transport_neutral_selected_value()
    {
        var selector = new BodySelector();
        await using var node = new FlowValueJsonSchemaValidatorNode(
            OrderSchema(),
            selector,
            valueSelector: "body");
        var output = Sink(node.Output);
        var body = Order("A-200", FlowValue.From(200L));
        var input = FlowValue.FromObject(new Dictionary<string, FlowValue>
        {
            ["body"] = body,
            ["source"] = FlowValue.From("test")
        });

        await node.Input.SendAsync(FlowMessage.Create(input));

        var result = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        selector.Calls.ShouldBe(1);
        selector.LastValueSelector.ShouldBe("body");
        result.IsError.ShouldBeFalse();
        result.Value.ShouldNotBeNull().Input.ShouldBeSameAs(input);
        result.Value.Value.ShouldBeSameAs(body);
        result.Value.ValueSelector.ShouldBe("body");
    }

    [Fact]
    public async Task Selector_failure_is_normal_and_later_input_continues()
    {
        await using var node = new FlowValueJsonSchemaValidatorNode(
            OrderSchema(),
            new FailOnceSelector());
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(Order("A-300", FlowValue.From(300L))));
        await node.Input.SendAsync(FlowMessage.Create(Order("A-301", FlowValue.From(301L))));

        var failure = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        var success = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        failure.Kind.ShouldBe(ValidationResultKinds.ValueSelectorFailed);
        failure.Error.ShouldNotBeNull().Code
            .ShouldBe(ValidationErrorCodeNames.ValueSelectorFailed);
        success.Kind.ShouldBe(ValidationResultKinds.Valid);
        success.IsError.ShouldBeFalse();
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Null_input_is_a_normal_error_result()
    {
        await using var node = new FlowValueJsonSchemaValidatorNode(OrderSchema());
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create<FlowValue>(null!));

        var result = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
        result.Kind.ShouldBe(ValidationResultKinds.MissingInput);
        result.Error.ShouldNotBeNull().Code.ShouldBe(ValidationErrorCodeNames.MissingInput);
    }

    [Fact]
    public async Task Scalar_flow_value_kinds_use_ordinary_json_schema_values()
    {
        var schema = JsonSchema.FromText("""
            {
              "type": "object",
              "required": ["enabled", "count", "ratio", "id"],
              "properties": {
                "enabled": { "type": "boolean" },
                "count": { "type": "integer" },
                "ratio": { "type": "number" },
                "id": { "type": "string" }
              }
            }
            """);
        await using var node = new FlowValueJsonSchemaValidatorNode(schema);
        var output = Sink(node.Output);
        var value = FlowValue.FromObject(new Dictionary<string, FlowValue>
        {
            ["enabled"] = FlowValue.From(true),
            ["count"] = FlowValue.From(12L),
            ["ratio"] = FlowValue.From(1.5m),
            ["id"] = FlowValue.From(Guid.Parse("8f748096-c629-4a04-a06f-c89b63c49931"))
        });

        await node.Input.SendAsync(FlowMessage.Create(value));

        (await output.ReceiveAsync().WaitAsync(Timeout)).Payload.Kind
            .ShouldBe(ValidationResultKinds.Valid);
    }

    [Fact]
    public async Task Structured_and_special_flow_value_kinds_use_json_schema_values()
    {
        var schema = JsonSchema.FromText("""
            {
              "type": "object",
              "required": ["items", "binary", "timestamp", "date", "time", "duration"],
              "properties": {
                "items": { "type": "array", "items": { "type": "integer" } },
                "binary": { "type": "string" },
                "timestamp": { "type": "string" },
                "date": { "type": "string" },
                "time": { "type": "string" },
                "duration": { "type": "string" }
              }
            }
            """);
        await using var node = new FlowValueJsonSchemaValidatorNode(schema);
        var output = Sink(node.Output);
        var value = FlowValue.FromObject(new Dictionary<string, FlowValue>
        {
            ["items"] = FlowValue.FromArray([FlowValue.From(1L), FlowValue.From(2L)]),
            ["binary"] = FlowValue.FromBinary(new byte[] { 1, 2, 3 }),
            ["timestamp"] = FlowValue.From(DateTimeOffset.Parse("2026-07-23T12:00:00Z")),
            ["date"] = FlowValue.From(new DateOnly(2026, 7, 23)),
            ["time"] = FlowValue.From(new TimeOnly(12, 30, 0)),
            ["duration"] = FlowValue.From(TimeSpan.FromMinutes(5))
        });

        await node.Input.SendAsync(FlowMessage.Create(value));

        (await output.ReceiveAsync().WaitAsync(Timeout)).Payload.Kind
            .ShouldBe(ValidationResultKinds.Valid);
    }

    [Fact]
    public async Task Output_fans_out_accepted_results_in_order()
    {
        await using var node = new FlowValueJsonSchemaValidatorNode(OrderSchema());
        var first = Sink(node.Output);
        var second = Sink(node.Output);
        var values = new[]
        {
            Order("A-500", FlowValue.From(500L)),
            Order("A-501", FlowValue.From("wrong"))
        };

        foreach (var value in values)
            await node.Input.SendAsync(FlowMessage.Create(value));

        var firstKinds = new[]
        {
            (await first.ReceiveAsync().WaitAsync(Timeout)).Payload.Kind,
            (await first.ReceiveAsync().WaitAsync(Timeout)).Payload.Kind
        };
        var secondKinds = new[]
        {
            (await second.ReceiveAsync().WaitAsync(Timeout)).Payload.Kind,
            (await second.ReceiveAsync().WaitAsync(Timeout)).Payload.Kind
        };
        firstKinds.ShouldBe([ValidationResultKinds.Valid, ValidationResultKinds.Invalid]);
        secondKinds.ShouldBe(firstKinds);
    }

    [Fact]
    public async Task Schema_path_is_loaded_before_processing()
    {
        var schemaPath = Path.Combine(
            Path.GetTempPath(),
            $"fluxflow-validation-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(schemaPath, OrderSchemaText());
        try
        {
            var options = new JsonSchemaValidatorOptions { SchemaPath = schemaPath };
            await using var node = new FlowValueJsonSchemaValidatorNode(
                options.LoadSchema(),
                schemaPath: options.SchemaPath,
                options: options);
            var output = Sink(node.Output);

            await node.Input.SendAsync(
                FlowMessage.Create(Order("A-600", FlowValue.From(600L))));

            var result = (await output.ReceiveAsync().WaitAsync(Timeout)).Payload;
            result.Kind.ShouldBe(ValidationResultKinds.Valid);
            result.Value.ShouldNotBeNull().Value.ShouldBeSameAs(result.Value.Input);
        }
        finally
        {
            File.Delete(schemaPath);
        }
    }

    [Fact]
    public void Schema_options_fail_fast_for_missing_or_malformed_schema()
    {
        Should.Throw<InvalidOperationException>(() =>
                JsonSchemaValidatorOptions.Default.LoadSchema())
            .Message.ShouldContain("schema or schemaPath is required");
        Should.Throw<InvalidOperationException>(() =>
                new JsonSchemaValidatorOptions
                {
                    Schema = JsonSerializer.SerializeToElement("{ not-json")
                }.LoadSchema())
            .Message.ShouldContain("could not load schema");
    }

    [Fact]
    public async Task Events_use_injected_clock_and_describe_result_kind()
    {
        var timestamp = DateTimeOffset.Parse("2026-07-18T18:00:00Z");
        await using var node = new FlowValueJsonSchemaValidatorNode(
            OrderSchema(),
            schemaId: "orders",
            clock: new FakeTimeProvider(timestamp));
        Sink(node.Output);
        var events = Sink(node.Events);

        await node.Input.SendAsync(FlowMessage.Create(Order("A-400", FlowValue.From("wrong"))));

        var @event = await events.ReceiveAsync().WaitAsync(Timeout);
        @event.Timestamp.ShouldBe(timestamp);
        @event.Name.ShouldBe(ValidationDiagnosticNames.JsonSchemaInvalid);
        @event.Level.ShouldBe(FlowEventLevel.Information);
        @event.Attributes["resultKind"].ShouldBe(ValidationResultKinds.Invalid);
        @event.Attributes["isError"].ShouldBe(false);
    }

    [Fact]
    public void Canonical_node_rejects_invalid_static_options_and_has_no_error_port()
    {
        Should.Throw<ArgumentNullException>(() =>
            new FlowValueJsonSchemaValidatorNode(null!));
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new FlowValueJsonSchemaValidatorNode(
                OrderSchema(),
                options: new JsonSchemaValidatorOptions { BoundedCapacity = 0 }))
            .Message.ShouldContain("boundedCapacity");
        Should.Throw<ArgumentException>(() =>
            new FlowValueJsonSchemaValidatorNode(
                OrderSchema(),
                options: new JsonSchemaValidatorOptions { InputType = " " }))
            .Message.ShouldContain("inputType");
        typeof(FlowValueJsonSchemaValidatorNode).GetProperty("Errors").ShouldBeNull();
        typeof(FlowValueJsonSchemaValidatorNode).GetProperty("Valid").ShouldBeNull();
        typeof(FlowValueJsonSchemaValidatorNode).GetProperty("Invalid").ShouldBeNull();
        typeof(JsonSchemaValidatorOptions).GetProperty("PayloadSelector").ShouldBeNull();
    }

    private static JsonSchema OrderSchema()
        => JsonSchema.FromText(OrderSchemaText());

    private static string OrderSchemaText()
        => """
            {
              "type": "object",
              "required": ["id", "total"],
              "properties": {
                "id": { "type": "string" },
                "total": { "type": "number" }
              }
            }
            """;

    private static FlowValue Order(string id, FlowValue total)
        => FlowValue.FromObject(new Dictionary<string, FlowValue>
        {
            ["id"] = FlowValue.From(id),
            ["total"] = total
        });

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink);
        return sink;
    }

    private sealed class BodySelector : IJsonSchemaFlowValueSelector
    {
        public int Calls { get; private set; }

        public string? LastValueSelector { get; private set; }

        public FlowValue Select(FlowValue input, JsonSchemaValidatorContext context)
        {
            Calls++;
            LastValueSelector = context.ValueSelector;
            return input.GetObject()["body"];
        }
    }

    private sealed class FailOnceSelector : IJsonSchemaFlowValueSelector
    {
        private int _calls;

        public FlowValue Select(FlowValue input, JsonSchemaValidatorContext context)
        {
            if (Interlocked.Increment(ref _calls) == 1)
                throw new InvalidOperationException("selection failed");
            return input;
        }
    }
}
