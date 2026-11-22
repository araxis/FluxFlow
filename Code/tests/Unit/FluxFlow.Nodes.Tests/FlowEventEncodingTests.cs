using FluxFlow.Core.Nodes;
using System.Globalization;
using System.Text.Json;
using FluxFlow.Core.Messaging;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;

public sealed class FlowEventEncodingTests
{
    [Fact]
    public void Event_and_nested_object_names_are_preserved_without_renaming_dictionary_keys()
    {
        using var details = JsonDocument.Parse("""{"MixedCase":1,"lower":2}""");
        var source = new FlowEvent
        {
            Name = "sample",
            Details = new JsonFlowValue(details.RootElement),
            Attributes = new Dictionary<string, object?> { ["original-key"] = new { QoS = 1, MixedCase = "kept" } },
            Dimensions = new Dictionary<string, object?> { ["lower"] = new { DeviceId = "sensor" } },
            Measurements = new Dictionary<string, double> { ["duration_ms"] = 3 }
        };
        var json = source.Json;
        json.EnumerateObject().Select(property => property.Name).ShouldBe(
            ["Name", "Kind", "Level", "Timestamp", "Message", "Dimensions", "Measurements", "Details", "Attributes"]);
        var attribute = json.GetProperty("Attributes").GetProperty("original-key");
        attribute.EnumerateObject().Select(property => property.Name).ShouldBe(["QoS", "MixedCase"]);
        json.GetProperty("Dimensions").GetProperty("lower").GetProperty("DeviceId").GetString().ShouldBe("sensor");
        json.GetProperty("Details").GetRawText().ShouldBe(details.RootElement.GetRawText());
        var restored = new JsonFlowValue(json).ToFlowEvent();
        restored.Name.ShouldBe(source.Name);
        restored.Attributes["original-key"].ShouldBeOfType<JsonElement>().GetProperty("QoS").GetInt32().ShouldBe(1);
        restored.Measurements["duration_ms"].ShouldBe(3);
    }

    [Fact]
    public void Generated_event_metadata_preserves_all_supported_scalar_encodings()
    {
        object?[] values =
        [
            null, "quoted \"text\"", true, 'x', byte.MaxValue, sbyte.MinValue,
            short.MinValue, ushort.MaxValue, int.MinValue, uint.MaxValue,
            long.MinValue, ulong.MaxValue, 1.25m, 1.25d, 1.25f,
            new DateTime(2026, 10, 18, 12, 30, 0, DateTimeKind.Utc),
            new DateTimeOffset(2026, 10, 18, 12, 30, 0, TimeSpan.FromHours(2)),
            TimeSpan.FromSeconds(12), Guid.Parse("ae7c9601-f37d-4eed-96c6-fcbf4ee02d4f")
        ];
        foreach (var value in values)
        {
            var source = new FlowEvent
            {
                Name = "scalar",
                Attributes = new Dictionary<string, object?> { ["value"] = value },
                Dimensions = new Dictionary<string, object?> { ["value"] = value }
            };
            var expected = JsonSerializer.Serialize(value);
            source.Json.GetProperty("Attributes").GetProperty("value").GetRawText().ShouldBe(expected);
            source.Json.GetProperty("Dimensions").GetProperty("value").GetRawText().ShouldBe(expected);
        }
    }

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
            Message = text,
            Dimensions = new Dictionary<string, object?> { ["text"] = text, [" "] = "ignored" },
            Attributes = new Dictionary<string, object?> { ["text"] = text, [""] = "ignored" }
        };
        var message = source.ToMessage(new("parent"));
        var root = message.Value!.Json;
        foreach (var name in new[] { "Dimensions", "Attributes" })
        {
            var map = root.GetProperty(name);
            map.EnumerateObject().Select(property => property.Name).ShouldBe(new[] { "text" });
            map.GetProperty("text").GetRawText().ShouldBe(JsonSerializer.Serialize(text));
        }
        root.GetProperty("Message").GetRawText().ShouldBe(JsonSerializer.Serialize(text));
        root.GetProperty("Name").GetString().ShouldBe("flow.test");
        message.Headers[FlowEventHeaders.Type].Json.GetString().ShouldBe("flow.test");
        message.Headers[FlowEventHeaders.Kind].Json.GetString().ShouldBe("diagnostic");
        message.Headers[FlowEventHeaders.Level].Json.GetString().ShouldBe("trace");
        message.Timestamp.ShouldBe(timestamp);
        message.CorrelationId.ShouldBe(new CorrelationId("parent"));
        source.Name.ShouldBe("flow.test");
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
            ["unsupportedFlowValue"] = new UnsupportedFlowValue(),
            ["undefined"] = default(JsonElement)
        };
        var source = new FlowEvent { Name = "snapshot", Dimensions = values, Attributes = values };
        var message = source.ToMessage();
        nested["number"] = 9;
        values["null"] = "changed";
        document.Dispose();

        var root = message.Value!.Json;
        foreach (var name in new[] { "Dimensions", "Attributes" })
        {
            var map = root.GetProperty(name);
            map.EnumerateObject().Count().ShouldBe(7);
            map.GetProperty("structured").GetProperty("number").GetInt32().ShouldBe(3);
            map.GetProperty("borrowed").GetRawText().ShouldBe("[1,true]");
            map.GetProperty("canonical").GetProperty("id").GetInt32().ShouldBe(42);
            map.GetProperty("null").ValueKind.ShouldBe(JsonValueKind.Null);
            map.GetProperty("unsupported").GetString().ShouldBe("invariant fallback");
            map.GetProperty("unsupportedFlowValue").GetString().ShouldBe("flow fallback");
            map.GetProperty("undefined").GetString().ShouldBe("");
        }
        source.Attributes["null"].ShouldBeNull();
        source.Dimensions["null"].ShouldBeNull();
    }

    [Fact]
    public void Empty_maps_keep_their_canonical_json_shape()
    {
        var source = new FlowEvent { Name = "empty" };
        var other = new FlowEvent { Name = "empty" };
        var dimensions = source.Dimensions;
        var attributes = source.Attributes;
        var measurements = source.Measurements;
        var root = source.ToMessage().Value!.Json;
        foreach (var name in new[] { "Dimensions", "Attributes", "Measurements" })
        {
            root.GetProperty(name).ValueKind.ShouldBe(JsonValueKind.Object);
            root.GetProperty(name).EnumerateObject().ShouldBeEmpty();
        }
        source.Dimensions.ShouldBeSameAs(dimensions);
        source.Attributes.ShouldBeSameAs(attributes);
        source.Measurements.ShouldBeSameAs(measurements);
        source.ShouldNotBe(other);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Nonfinite_event_measurements_are_rejected(double value)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new FlowEvent
        {
            Name = "invalid", Measurements = new Dictionary<string, double> { ["duration"] = value }
        }).Message.ShouldContain("finite");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Invalid_event_field_names_retain_validation(bool measurement)
    {
        Should.Throw<ArgumentException>(() => new FlowEvent
        {
            Name = measurement ? "valid" : " ",
            Measurements = measurement ? new Dictionary<string, double> { [" "] = 1 } : new Dictionary<string, double>()
        });
    }

    private sealed class UnsupportedFlowValue : FlowValue
    {
        protected override JsonElement CreateJson() => throw new NotSupportedException("Cannot serialize this value.");
        public override string ToString() => "flow fallback";
    }

    private sealed class UnsupportedValue : IFormattable
    {
        public string Value => throw new NotSupportedException("Cannot serialize this value.");
        public string ToString(string? format, IFormatProvider? provider)
            => ReferenceEquals(provider, CultureInfo.InvariantCulture) ? "invariant fallback" : "wrong culture";
    }
}
