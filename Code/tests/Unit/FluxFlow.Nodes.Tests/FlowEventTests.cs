using FluxFlow.Core.Nodes;
using FluxFlow.Core.Messaging;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;

public sealed class FlowEventTests
{
    [Fact]
    public void Reused_event_keeps_its_timestamp_and_independent_envelopes()
    {
        var value = new FlowEvent { Name = "created" };
        var first = value.ToMessage(new("first"));
        var second = value.ToMessage(new("second"));

        first.Value.ShouldBeSameAs(value);
        second.Value.ShouldBeSameAs(value);
        first.MessageId.ShouldNotBe(second.MessageId);
        first.CorrelationId.ShouldBe(new CorrelationId("first"));
        second.CorrelationId.ShouldBe(new CorrelationId("second"));
        first.Timestamp.ShouldBe(value.Timestamp);
        second.Timestamp.ShouldBe(value.Timestamp);
        value.Timestamp.ShouldNotBe(default);
    }

    [Fact]
    public void Event_maps_own_their_data_before_publication_and_after_json_read()
    {
        var nested = new[] { 1, 2 };
        var fields = new Dictionary<string, object?> { ["items"] = nested };
        var measurements = new Dictionary<string, double> { ["duration"] = 3 };
        var value = new FlowEvent
        {
            Name = "snapshot", Attributes = fields, Dimensions = fields, Measurements = measurements
        };
        nested[0] = 99;
        fields.Clear();
        measurements["duration"] = 99;

        value.Attributes["items"].ShouldBeOfType<JsonElement>()[0].GetInt32().ShouldBe(1);
        value.Dimensions["items"].ShouldBeOfType<JsonElement>()[0].GetInt32().ShouldBe(1);
        value.Measurements["duration"].ShouldBe(3);
        var json = value.Json;
        Should.Throw<NotSupportedException>(() => ((IDictionary<string, object?>)value.Attributes).Clear());
        Should.Throw<NotSupportedException>(() => ((IDictionary<string, double>)value.Measurements).Clear());
        value.Json.ShouldBe(json);
        json.GetProperty("Measurements").GetProperty("duration").GetDouble().ShouldBe(3);
    }

    [Fact]
    public void Attributes_AreCopiedOnAssignment()
    {
        var attributes = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["kind"] = "original"
        };

        var flowEvent = new FlowEvent
        {
            Name = "node.event",
            Attributes = attributes
        };

        attributes["kind"] = "changed";
        attributes["new"] = "later";

        flowEvent.Attributes["kind"].ShouldBe("original");
        flowEvent.Attributes.ContainsKey("new").ShouldBeFalse();
    }

    [Fact]
    public void Attributes_UseOrdinalKeysAfterAssignment()
    {
        var flowEvent = new FlowEvent
        {
            Name = "node.event",
            Attributes = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Kind"] = "diagnostic"
            }
        };

        flowEvent.Attributes.ContainsKey("Kind").ShouldBeTrue();
        flowEvent.Attributes.ContainsKey("kind").ShouldBeFalse();
    }
}
