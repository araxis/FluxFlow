using System.Text.Json;
using System.Threading.Tasks.Dataflow;
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

public sealed class JsonSchemaValidatorNodeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Valid_and_invalid_json_values_are_normal_domain_results()
    {
        await using var node = new JsonSchemaValidatorNode(OrderSchema());
        var output = Sink(node.Output);
        var validValue = Order("A-100", 125L);
        var invalidValue = Order("A-101", "wrong");
        var validInput = FlowMessage.Create(validValue, new CorrelationId("valid-order"));
        var invalidInput = FlowMessage.Create(invalidValue, new CorrelationId("invalid-order"));

        await node.Input.SendAsync(validInput);
        await node.Input.SendAsync(invalidInput);

        var valid = await output.ReceiveAsync().WaitAsync(Timeout);
        var invalid = await output.ReceiveAsync().WaitAsync(Timeout);
        valid.IsError.ShouldBeFalse();
        valid.Value.IsValid.ShouldBeTrue();
        valid.Value.Input.ShouldBe(validValue);
        valid.Value.Value.ShouldBe(validValue);
        valid.CorrelationId.ShouldBe(validInput.CorrelationId);
        valid.TraceId.ShouldBe(validInput.TraceId);
        valid.CausationId.ShouldBe(validInput.MessageId);
        invalid.IsError.ShouldBeFalse();
        invalid.Value.IsValid.ShouldBeFalse();
        invalid.Value.Issues.ShouldNotBeEmpty();
        invalid.CorrelationId.ShouldBe(invalidInput.CorrelationId);
    }

    [Fact]
    public async Task Selector_returns_the_transport_neutral_selected_json_value()
    {
        var selector = new BodySelector();
        await using var node = new JsonSchemaValidatorNode(
            OrderSchema(),
            selector,
            valueSelector: "body");
        var output = Sink(node.Output);
        var body = Order("A-200", 200L);
        var input = Json(new { body, source = "test" });

        await node.Input.SendAsync(FlowMessage.Create(input));

        var result = (await output.ReceiveAsync().WaitAsync(Timeout)).Value;
        selector.Calls.ShouldBe(1);
        selector.LastValueSelector.ShouldBe("body");
        result.Input.ShouldBe(input);
        result.Value.GetRawText().ShouldBe(body.GetRawText());
        result.ValueSelector.ShouldBe("body");
    }

    [Fact]
    public async Task Selector_failure_is_in_band_and_later_input_continues()
    {
        await using var node = new JsonSchemaValidatorNode(
            OrderSchema(),
            new FailOnceSelector());
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(Order("A-300", 300L)));
        await node.Input.SendAsync(FlowMessage.Create(Order("A-301", 301L)));

        var failure = await output.ReceiveAsync().WaitAsync(Timeout);
        var success = await output.ReceiveAsync().WaitAsync(Timeout);
        failure.IsError.ShouldBeTrue();
        failure.Error!.Code.ShouldBe(ValidationErrorCodeNames.ValueSelectorFailed);
        success.Value.IsValid.ShouldBeTrue();
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Incoming_error_is_propagated()
    {
        await using var node = new JsonSchemaValidatorNode(OrderSchema());
        var output = Sink(node.Output);
        var error = new FlowError(
            "upstream.failed",
            "Input was unavailable.",
            "Validation",
            isTransient: false);

        await node.Input.SendAsync(FlowMessage.CreateError<JsonElement>(error));

        var result = await output.ReceiveAsync().WaitAsync(Timeout);
        result.IsError.ShouldBeTrue();
        result.Error.ShouldBeSameAs(error);
    }

    [Fact]
    public async Task Ordinary_json_scalar_and_structured_values_validate_directly()
    {
        var schema = JsonSchema.FromText("""
            {
              "type": "object",
              "required": ["enabled", "count", "ratio", "id", "items", "binary"],
              "properties": {
                "enabled": { "type": "boolean" },
                "count": { "type": "integer" },
                "ratio": { "type": "number" },
                "id": { "type": "string" },
                "items": { "type": "array", "items": { "type": "integer" } },
                "binary": { "type": "string" }
              }
            }
            """);
        await using var node = new JsonSchemaValidatorNode(schema);
        var output = Sink(node.Output);
        var value = Json(new
        {
            enabled = true,
            count = 12L,
            ratio = 1.5m,
            id = Guid.Parse("8f748096-c629-4a04-a06f-c89b63c49931"),
            items = new[] { 1L, 2L },
            binary = new byte[] { 1, 2, 3 }
        });

        await node.Input.SendAsync(FlowMessage.Create(value));

        (await output.ReceiveAsync().WaitAsync(Timeout)).Value.IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task Output_fans_out_accepted_results_in_order()
    {
        await using var node = new JsonSchemaValidatorNode(OrderSchema());
        var first = Sink(node.Output);
        var second = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(Order("A-500", 500L)));
        await node.Input.SendAsync(FlowMessage.Create(Order("A-501", "wrong")));

        var firstValidity = new[]
        {
            (await first.ReceiveAsync().WaitAsync(Timeout)).Value.IsValid,
            (await first.ReceiveAsync().WaitAsync(Timeout)).Value.IsValid
        };
        var secondValidity = new[]
        {
            (await second.ReceiveAsync().WaitAsync(Timeout)).Value.IsValid,
            (await second.ReceiveAsync().WaitAsync(Timeout)).Value.IsValid
        };
        firstValidity.ShouldBe([true, false]);
        secondValidity.ShouldBe(firstValidity);
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
            await using var node = new JsonSchemaValidatorNode(
                options.LoadSchema(),
                schemaPath: options.SchemaPath,
                options: options);
            var output = Sink(node.Output);

            await node.Input.SendAsync(FlowMessage.Create(Order("A-600", 600L)));

            var result = (await output.ReceiveAsync().WaitAsync(Timeout)).Value;
            result.IsValid.ShouldBeTrue();
            result.Value.ShouldBe(result.Input);
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
    public async Task Events_use_injected_clock_and_describe_domain_result_kind()
    {
        var timestamp = DateTimeOffset.Parse("2026-07-18T18:00:00Z");
        await using var node = new JsonSchemaValidatorNode(
            OrderSchema(),
            schemaId: "orders",
            clock: new FakeTimeProvider(timestamp));
        Sink(node.Output);
        var events = Sink(node.Events);

        await node.Input.SendAsync(FlowMessage.Create(Order("A-400", "wrong")));

        var @event = await events.ReceiveAsync().WaitAsync(Timeout);
        @event.Timestamp.ShouldBe(timestamp);
        @event.Name.ShouldBe(ValidationDiagnosticNames.JsonSchemaInvalid);
        @event.Level.ShouldBe(FlowEventLevel.Information);
        @event.Attributes["resultKind"].ShouldBe(ValidationResultKinds.Invalid);
        @event.Attributes["isError"].ShouldBe(false);
    }

    [Fact]
    public void Canonical_node_rejects_invalid_static_options_and_has_no_branch_ports()
    {
        Should.Throw<ArgumentNullException>(() => new JsonSchemaValidatorNode(null!));
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new JsonSchemaValidatorNode(
                OrderSchema(),
                options: new JsonSchemaValidatorOptions { BoundedCapacity = 0 }))
            .Message.ShouldContain("positive");
        typeof(JsonSchemaValidatorNode).GetProperty("Errors").ShouldBeNull();
        typeof(JsonSchemaValidatorNode).GetProperty("Valid").ShouldBeNull();
        typeof(JsonSchemaValidatorNode).GetProperty("Invalid").ShouldBeNull();
        typeof(JsonSchemaValidatorOptions).GetProperty("PayloadSelector").ShouldBeNull();
    }

    private static JsonSchema OrderSchema() => JsonSchema.FromText(OrderSchemaText());

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

    private static JsonElement Order(string id, object total)
        => Json(new { id, total });

    private static JsonElement Json<T>(T value)
        => JsonSerializer.SerializeToElement(value);

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink);
        return sink;
    }

    private sealed class BodySelector : IJsonSchemaValueSelector
    {
        public int Calls { get; private set; }
        public string? LastValueSelector { get; private set; }

        public JsonElement Select(JsonElement input, JsonSchemaValidatorContext context)
        {
            Calls++;
            LastValueSelector = context.ValueSelector;
            return input.GetProperty("body");
        }
    }

    private sealed class FailOnceSelector : IJsonSchemaValueSelector
    {
        private int _calls;

        public JsonElement Select(JsonElement input, JsonSchemaValidatorContext context)
        {
            if (Interlocked.Increment(ref _calls) == 1)
                throw new InvalidOperationException("selection failed");
            return input;
        }
    }
}
