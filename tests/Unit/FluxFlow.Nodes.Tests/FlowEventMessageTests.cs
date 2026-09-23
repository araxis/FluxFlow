using FluxFlow.Core.Nodes;
using System.Text.Json;
using FluxFlow.Core.Messaging;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;

public sealed class FlowEventMessageTests
{
    [Fact]
    public void Value_projection_preserves_payload_time_and_uses_no_envelope_correlation()
    {
        var timestamp = new DateTimeOffset(2026, 9, 4, 12, 30, 0, TimeSpan.Zero);
        var message = new FlowEvent { Name = "stored", Timestamp = timestamp, CorrelationId = new("request") }.ToMessage();

        var projected = message.Value.ToFlowEvent();
        projected.Timestamp.ShouldBe(timestamp);
        projected.CorrelationId.ShouldBeNull();

        FlowValue withoutTime = new JsonFlowValue(JsonSerializer.SerializeToElement(new { type = "received" }));
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
    public void Typed_projection_keeps_envelope_time_while_canonical_projection_keeps_payload_time()
    {
        var occurredAt = new DateTimeOffset(2026, 9, 4, 12, 30, 0, TimeSpan.Zero);
        var value = new FlowEvent { Name = "stored", Timestamp = occurredAt }.ToMessage().Value;
        var typed = FlowMessage.Create<FlowValue>(value, new CorrelationId("request"));

        var typedEvent = typed.ToFlowEvent();
        typedEvent.Timestamp.ShouldBe(typed.Timestamp);
        typedEvent.CorrelationId.ShouldBe(typed.CorrelationId);
        var canonicalEvent = ((FlowMessage)typed).ToFlowEvent();
        canonicalEvent.Timestamp.ShouldBe(occurredAt);
        canonicalEvent.CorrelationId.ShouldBe(typed.CorrelationId);
    }

    [Fact]
    public void Event_round_trips_through_canonical_message_contract()
    {
        var timestamp = new DateTimeOffset(2026, 9, 4, 12, 30, 0, TimeSpan.Zero);
        var source = new FlowEvent
        {
            Timestamp = timestamp,
            CorrelationId = new CorrelationId("correlation-1"),
            Name = "orders.received",
            Kind = FlowEventKind.Domain,
            Level = FlowEventLevel.Information,
            Message = "Order received.",
            Dimensions = new Dictionary<string, object?> { ["tenant"] = "north" },
            Measurements = new Dictionary<string, double> { ["items"] = 3 },
            Details = ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new { orderId = "A-100" }, new JsonSerializerOptions(JsonSerializerDefaults.Web)))),
            Attributes = new Dictionary<string, object?> { ["attempt"] = 2 }
        };

        var message = source.ToMessage();

        message.IsError.ShouldBeFalse();
        message.Headers[FlowEventHeaders.Type].Json.GetString().ShouldBe("orders.received");
        message.Headers[FlowEventHeaders.Kind].Json.GetString().ShouldBe("domain");
        message.Headers[FlowEventHeaders.Level].Json.GetString().ShouldBe("information");
        message.Timestamp.ShouldBe(timestamp);
        message.CorrelationId.ShouldBe(new CorrelationId("correlation-1"));

        message.TryGetFlowEvent(out var projected).ShouldBeTrue();
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
