using System.Text.Json;
using System.Text.Json.Serialization;
using FluxFlow.Core.Messaging;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;

public sealed class FlowValueTests
{
    [Fact]
    public void From_and_deserialize_use_the_same_web_json_contract()
    {
        var value = ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new SamplePayload("alpha", 7), new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        var json = value.Json;
        json.GetProperty("name").GetString().ShouldBe("alpha");
        json.GetProperty("count").GetInt32().ShouldBe(7);
        value.Json.Deserialize<SamplePayload>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(new SamplePayload("alpha", 7));
    }

    [Fact]
    public void From_json_owns_a_clone_of_the_supplied_document()
    {
        FlowValue value;
        using (var document = JsonDocument.Parse("{\"enabled\":true}"))
            value = ((FlowValue)new JsonFlowValue(document.RootElement));

        value.Json.ValueKind.ShouldBe(JsonValueKind.Object);
        value.Json.GetProperty("enabled").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public void Default_json_projection_reuses_owned_storage_after_source_disposal()
    {
        FlowValue value;
        using (var document = JsonDocument.Parse("""{"items":["alpha",42,null],"enabled":true}"""))
            value = ((FlowValue)new JsonFlowValue(document.RootElement));

        var projected = value.Json;

        projected.ShouldBe(value.Json);
        value.Json.ShouldBe(projected);
        projected.GetProperty("items")[0].GetString().ShouldBe("alpha");
        projected.GetProperty("items")[1].GetInt32().ShouldBe(42);
        projected.GetProperty("items")[2].ValueKind.ShouldBe(JsonValueKind.Null);
        projected.GetProperty("enabled").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public void Explicit_json_projection_options_use_the_configured_converter()
    {
        var converter = new ConfiguredJsonElementConverter();
        var options = new JsonSerializerOptions();
        options.Converters.Add(converter);
        var value = ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new { Enabled = true }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        var configured = value.Json.Deserialize<JsonElement>(options);

        configured.GetString().ShouldBe("configured");
        converter.ReadCount.ShouldBe(1);
        value.Json.GetProperty("enabled").GetBoolean().ShouldBeTrue();
        converter.ReadCount.ShouldBe(1);
    }

    [Fact]
    public void Convert_value_changes_only_the_payload_representation()
    {
        var original = FlowMessage.Create(new ScalarValue<SamplePayload>(new SamplePayload("alpha", 7)),
            correlationId: CorrelationId.New(),
            headers: new Dictionary<string, FlowValue> { ["source"] = "test" });

        var converted = original.ConvertValue((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(original.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        converted.Value!.Json.Deserialize<SamplePayload>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(original.Value.Data);
        converted.CorrelationId.ShouldBe(original.CorrelationId);
        converted.TraceId.ShouldBe(original.TraceId);
        converted.MessageId.ShouldBe(original.MessageId);
        converted.CausationId.ShouldBe(original.CausationId);
        converted.Timestamp.ShouldBe(original.Timestamp);
        converted.Headers.ShouldBe(original.Headers);
    }

    [Fact]
    public void From_uses_the_concrete_runtime_type_for_polymorphic_values()
    {
        PolymorphicPayload payload = new ConcretePayload("runtime");

        var value = ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(payload, payload.GetType(), new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        value.Json.GetProperty("name").GetString().ShouldBe("runtime");
    }

    [Fact]
    public void From_snapshots_mutable_nested_collections()
    {
        var tags = new List<string> { "original", "second" };
        var source = new Dictionary<string, object?>
        {
            ["nested"] = new { Tags = tags },
            ["optional"] = null
        };

        var value = ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(source, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        tags[0] = "mutated";
        tags.Add("later");
        source["optional"] = "changed";

        var json = value.Json;
        json.GetProperty("nested").GetProperty("tags").EnumerateArray()
            .Select(item => item.GetString()).ShouldBe(["original", "second"]);
        json.GetProperty("optional").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public void Independently_owned_equal_json_does_not_imply_record_equality()
    {
        var first = ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new { Value = 42 }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var second = ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new { Value = 42 }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        first.Json.GetRawText().ShouldBe("{\"value\":42}");
        second.Json.GetRawText().ShouldBe("{\"value\":42}");
        (first == second).ShouldBeFalse();
        first.Equals(null).ShouldBeFalse();
        var sharedStorage = ((FlowValue)new JsonFlowValue(first.Json));
        sharedStorage.ShouldNotBeSameAs(first);
        sharedStorage.Equals(first).ShouldBeFalse();
    }

    private sealed class ConfiguredJsonElementConverter : JsonConverter<JsonElement>
    {
        public int ReadCount { get; private set; }

        public override JsonElement Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            ReadCount++;
            return JsonSerializer.SerializeToElement("configured");
        }

        public override void Write(
            Utf8JsonWriter writer,
            JsonElement value,
            JsonSerializerOptions options) => throw new NotSupportedException();
    }

    private sealed record SamplePayload(string Name, int Count);

    private abstract record PolymorphicPayload;

    private sealed record ConcretePayload(string Name) : PolymorphicPayload;
}
