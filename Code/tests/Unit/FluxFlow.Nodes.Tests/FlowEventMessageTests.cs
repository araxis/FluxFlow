using FluxFlow.Core.Nodes;
using System.Text.Json;
using FluxFlow.Core.Messaging;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;

public sealed class FlowEventMessageTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("\"not a timestamp\"")]
    public void Missing_or_invalid_timestamp_uses_envelope_time(string? timestamp)
    {
        using var document = JsonDocument.Parse(timestamp is null
            ? "{\"Name\":\"received\"}"
            : "{\"Name\":\"received\",\"Timestamp\":" + timestamp + "}");
        var message = FlowMessage.Create<FlowValue>(new JsonFlowValue(document.RootElement), new CorrelationId("request"));
        message.TryGetFlowEvent(out var typed).ShouldBeTrue();
        typed!.Timestamp.ShouldBe(message.Timestamp);
        ((FlowMessage)message).TryGetFlowEvent(out var canonical).ShouldBeTrue();
        canonical!.Timestamp.ShouldBe(message.Timestamp);
        message.CorrelationId.ShouldBe(new CorrelationId("request"));
    }

    [Fact]
    public void Optional_measurements_ignore_non_numeric_values()
    {
        using var document = JsonDocument.Parse("""
            {"Name":"measured","Measurements":{"valid":1.5,"text":"2","flag":true,"empty":null,"object":{},"array":[]}}
            """);
        FlowValue value = new JsonFlowValue(document.RootElement);
        value.TryGetFlowEvent(out var projected).ShouldBeTrue();
        projected!.Measurements.Count.ShouldBe(1);
        projected.Measurements["valid"].ShouldBe(1.5);
    }

    [Fact]
    public void Event_attributes_and_dimensions_preserve_integer_precision_recursively()
    {
        var fields = new Dictionary<string, object?>
        {
            ["max"] = long.MaxValue,
            ["min"] = long.MinValue,
            ["fraction"] = 1.25,
            ["array"] = new[] { long.MaxValue },
            ["object"] = new { value = long.MinValue }
        };
        var projected = new FlowEvent { Name = "numbers", Attributes = fields, Dimensions = fields }
            .Json;
        var restored = new JsonFlowValue(projected).ToFlowEvent();
        foreach (var values in new[] { restored.Attributes, restored.Dimensions })
        {
            values["max"].ShouldBeOfType<long>().ShouldBe(long.MaxValue);
            values["min"].ShouldBeOfType<long>().ShouldBe(long.MinValue);
            values["fraction"].ShouldBeOfType<double>().ShouldBe(1.25);
            values["array"].ShouldBeOfType<JsonElement>()[0].GetInt64().ShouldBe(long.MaxValue);
            values["object"].ShouldBeOfType<JsonElement>().GetProperty("value").GetInt64().ShouldBe(long.MinValue);
        }
    }

    [Fact]
    public void Value_projection_preserves_payload_time_and_uses_no_envelope_correlation()
    {
        var timestamp = new DateTimeOffset(2026, 9, 4, 12, 30, 0, TimeSpan.Zero);
        var message = new FlowEvent { Name = "stored", Timestamp = timestamp }.ToMessage(new("request"));

        var projected = message.Value.ToFlowEvent();
        projected.Timestamp.ShouldBe(timestamp);
        projected.ShouldBeSameAs(message.Value);
        message.CorrelationId.ShouldBe(new CorrelationId("request"));

        FlowValue withoutTime = new JsonFlowValue(JsonSerializer.SerializeToElement(new { Name = "received" }));
        var before = DateTimeOffset.UtcNow;
        var received = withoutTime.ToFlowEvent();
        received.Timestamp.ShouldBeGreaterThanOrEqualTo(before);
        received.Timestamp.ShouldBeLessThanOrEqualTo(DateTimeOffset.UtcNow);
        received.Kind.ShouldBe(FlowEventKind.Diagnostic);
        received.Level.ShouldBe(FlowEventLevel.Information);

        FlowValue error = new FlowError("test.error", "Expected error", "test", false);
        error.TryGetFlowEvent(out var rejected).ShouldBeFalse();
        rejected.ShouldBeNull();
    }

    [Fact]
    public void Typed_and_canonical_projections_use_payload_time_without_changing_envelope()
    {
        var occurredAt = new DateTimeOffset(2026, 9, 4, 12, 30, 0, TimeSpan.Zero);
        var value = new FlowEvent { Name = "stored", Timestamp = occurredAt }.ToMessage().Value;
        var typed = FlowMessage.Create<FlowValue>(value, new CorrelationId("request"));
        var envelopeTime = typed.Timestamp;
        envelopeTime.ShouldNotBe(occurredAt);

        var typedEvent = typed.ToFlowEvent();
        typedEvent.Timestamp.ShouldBe(occurredAt);
        typedEvent.ShouldBeSameAs(value);
        var canonicalEvent = ((FlowMessage)typed).ToFlowEvent();
        canonicalEvent.Timestamp.ShouldBe(occurredAt);
        canonicalEvent.ShouldBeSameAs(value);
        typed.CorrelationId.ShouldBe(new CorrelationId("request"));
        typed.Timestamp.ShouldBe(envelopeTime);
    }

    [Fact]
    public void Event_round_trips_through_canonical_message_contract()
    {
        var timestamp = new DateTimeOffset(2026, 9, 4, 12, 30, 0, TimeSpan.Zero);
        var source = new FlowEvent
        {
            Timestamp = timestamp,
            Name = "orders.received",
            Kind = FlowEventKind.Domain,
            Level = FlowEventLevel.Information,
            Message = "Order received.",
            Dimensions = new Dictionary<string, object?> { ["tenant"] = "north" },
            Measurements = new Dictionary<string, double> { ["items"] = 3 },
            Details = ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new { orderId = "A-100" }, new JsonSerializerOptions(JsonSerializerDefaults.Web)))),
            Attributes = new Dictionary<string, object?> { ["attempt"] = 2 }
        };

        var message = source.ToMessage(new CorrelationId("correlation-1"));

        message.IsError.ShouldBeFalse();
        message.Headers[FlowEventHeaders.Type].Json.GetString().ShouldBe("orders.received");
        message.Headers[FlowEventHeaders.Kind].Json.GetString().ShouldBe("domain");
        message.Headers[FlowEventHeaders.Level].Json.GetString().ShouldBe("information");
        message.Timestamp.ShouldBe(timestamp);
        message.CorrelationId.ShouldBe(new CorrelationId("correlation-1"));

        message.Value.ShouldBeSameAs(source);
        FlowMessage.Create((FlowValue)new JsonFlowValue(message.Value.Json)).TryGetFlowEvent(out var projected).ShouldBeTrue();
        projected.ShouldNotBeNull();
        projected.Name.ShouldBe(source.Name);
        projected.Kind.ShouldBe(source.Kind);
        projected.Level.ShouldBe(source.Level);
        projected.Dimensions["tenant"].ShouldBe("north");
        projected.Measurements["items"].ShouldBe(3);
        projected.Attributes["attempt"].ShouldBe(2L);
        projected.Details.Json.GetProperty("orderId").GetString().ShouldBe("A-100");
    }

    [Fact]
    public void Non_event_message_does_not_project_as_event()
    {
        var message = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new { value = 42 }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        message.TryGetFlowEvent(out var projected).ShouldBeFalse();
        projected.ShouldBeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Typed_helper_error_and_null_payload_do_not_throw_from_try_projection(bool isError)
    {
        var message = isError
            ? TestMessages.Error<FlowValue>(new FlowError("test.error", "Expected error", "test", false))
            : FlowMessage.Create<FlowValue>(JsonFlowValue.Null);

        message.TryGetFlowEvent(out var projected).ShouldBeFalse();
        projected.ShouldBeNull();
        var failure = Should.Throw<InvalidOperationException>(() => message.ToFlowEvent());
        failure.Message.ShouldContain("canonical event value");
    }
}
