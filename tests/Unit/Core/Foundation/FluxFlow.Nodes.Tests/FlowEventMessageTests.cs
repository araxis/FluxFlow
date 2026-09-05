using FluxFlow.Data;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;

public sealed class FlowEventMessageTests
{
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
            Details = FlowValue.From(new { orderId = "A-100" }),
            Attributes = new Dictionary<string, object?> { ["attempt"] = 2 }
        };

        var message = source.ToMessage();

        message.IsError.ShouldBeFalse();
        message.Headers[FlowEventHeaders.Type].ShouldBe("orders.received");
        message.Headers[FlowEventHeaders.Kind].ShouldBe("domain");
        message.Headers[FlowEventHeaders.Level].ShouldBe("information");
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
        projected.Details.ToJsonElement().GetProperty("orderId").GetString().ShouldBe("A-100");
    }

    [Fact]
    public void Non_event_message_does_not_project_as_event()
    {
        var message = FlowMessage.Create(FlowValue.From(new { value = 42 }));

        message.TryGetFlowEvent(out var projected).ShouldBeFalse();
        projected.ShouldBeNull();
    }
}
