using FluxFlow.Core.Nodes;
using System.Globalization;
using System.Text.Json;
using FluxFlow.Core.Messaging;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;

public sealed class FlowEventEncodingTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("quote\"\n\\<tag> Ω 😀")]
    [InlineData("2026-09-11T10:20:30+00:00")]
    public void Scalar_event_fields_keep_canonical_encoding(string? text)
    {
        var timestamp = new DateTimeOffset(2026, 9, 11, 10, 20, 30, TimeSpan.Zero);
        var source = new FlowEvent
        {
            Name = "  flow.test  ",
            Timestamp = timestamp,
            Level = FlowEventLevel.Trace,
            CorrelationId = new("parent"),
            Message = text,
            Dimensions = new Dictionary<string, object?> { ["text"] = text, [" "] = "ignored" },
            Attributes = new Dictionary<string, object?> { ["text"] = text, [""] = "ignored" }
        };
        var message = source.ToMessage();
        var root = message.Value!.Json;
        foreach (var name in new[] { "dimensions", "attributes" })
        {
            var map = root.GetProperty(name);
            map.EnumerateObject().Select(property => property.Name).ShouldBe(new[] { "text" });
            map.GetProperty("text").GetRawText().ShouldBe(JsonSerializer.Serialize(text));
        }
        root.GetProperty("message").GetRawText().ShouldBe(JsonSerializer.Serialize(text));
        root.GetProperty("type").GetString().ShouldBe("flow.test");
        message.Headers[FlowEventHeaders.Type].Json.GetString().ShouldBe("flow.test");
        message.Headers[FlowEventHeaders.Kind].Json.GetString().ShouldBe("diagnostic");
        message.Headers[FlowEventHeaders.Level].Json.GetString().ShouldBe("trace");
        message.Timestamp.ShouldBe(timestamp);
        message.CorrelationId.ShouldBe(new CorrelationId("parent"));
        source.Name.ShouldBe("  flow.test  ");
    }

    [Fact]
    public void Complex_event_fields_keep_snapshots_and_fallbacks()
    {
        using var document = JsonDocument.Parse("[1,true]");
        var nested = new Dictionary<string, object?> { ["number"] = 3 };
        var values = new Dictionary<string, object?>
        {
            ["structured"] = nested,
            ["borrowed"] = document.RootElement,
            ["canonical"] = ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new { id = 42 }, new JsonSerializerOptions(JsonSerializerDefaults.Web)))),
            ["null"] = null,
            ["unsupported"] = new UnsupportedValue(),
            ["undefined"] = default(JsonElement)
        };
        var source = new FlowEvent { Name = "snapshot", Dimensions = values, Attributes = values };
        var message = source.ToMessage();
        nested["number"] = 9;
        values["null"] = "changed";
        document.Dispose();

        var root = message.Value!.Json;
        foreach (var name in new[] { "dimensions", "attributes" })
        {
            var map = root.GetProperty(name);
            map.EnumerateObject().Count().ShouldBe(6);
            map.GetProperty("structured").GetProperty("number").GetInt32().ShouldBe(3);
            map.GetProperty("borrowed").GetRawText().ShouldBe("[1,true]");
            map.GetProperty("canonical").GetProperty("id").GetInt32().ShouldBe(42);
            map.GetProperty("null").ValueKind.ShouldBe(JsonValueKind.Null);
            map.GetProperty("unsupported").GetString().ShouldBe("invariant fallback");
            map.GetProperty("undefined").GetString().ShouldBe("");
        }
        source.Attributes["null"].ShouldBeNull();
        source.Dimensions["null"].ShouldBe("changed");
    }

    [Fact]
    public void Empty_serialization_maps_preserve_definition_identity()
    {
        var source = new FlowEvent { Name = "empty" };
        var other = new FlowEvent { Name = "empty" };
        var dimensions = source.Dimensions;
        var attributes = source.Attributes;
        var measurements = source.Measurements;
        var root = source.ToMessage().Value!.Json;
        foreach (var name in new[] { "dimensions", "attributes", "measurements" })
        {
            root.GetProperty(name).ValueKind.ShouldBe(JsonValueKind.Object);
            root.GetProperty(name).EnumerateObject().ShouldBeEmpty();
        }
        source.Dimensions.ShouldBeSameAs(dimensions);
        source.Attributes.ShouldBeSameAs(attributes);
        source.Measurements.ShouldBeSameAs(measurements);
        source.ShouldNotBe(other);
        source.Dimensions.ShouldNotBeSameAs(other.Dimensions);
        source.Attributes.ShouldNotBeSameAs(other.Attributes);
        source.Measurements.ShouldNotBeSameAs(other.Measurements);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Nonfinite_event_measurements_are_rejected(double value)
    {
        var source = new FlowEvent { Name = "invalid", Measurements = new Dictionary<string, double> { ["duration"] = value } };
        Should.Throw<ArgumentOutOfRangeException>(() => source.ToMessage()).Message.ShouldContain("finite");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Invalid_event_field_names_retain_validation(bool measurement)
    {
        var source = new FlowEvent
        {
            Name = measurement ? "valid" : " ",
            Measurements = measurement ? new Dictionary<string, double> { [" "] = 1 } : new Dictionary<string, double>()
        };
        Should.Throw<ArgumentException>(() => source.ToMessage());
    }

    private sealed class UnsupportedValue : IFormattable
    {
        public string Value => throw new NotSupportedException("Cannot serialize this value.");
        public string ToString(string? format, IFormatProvider? provider)
            => ReferenceEquals(provider, CultureInfo.InvariantCulture) ? "invariant fallback" : "wrong culture";
    }
}
