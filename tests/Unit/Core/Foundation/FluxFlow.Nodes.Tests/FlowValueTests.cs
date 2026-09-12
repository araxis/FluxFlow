using System.Text.Json;
using System.Text.Json.Serialization;
using FluxFlow.Data;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;

public sealed class FlowValueTests
{
    [Fact]
    public void From_and_deserialize_use_the_same_web_json_contract()
    {
        var value = FlowValue.From(new SamplePayload("alpha", 7));

        var json = value.ToJsonElement();
        json.GetProperty("name").GetString().ShouldBe("alpha");
        json.GetProperty("count").GetInt32().ShouldBe(7);
        value.Deserialize<SamplePayload>().ShouldBe(new SamplePayload("alpha", 7));
    }

    [Fact]
    public void From_json_owns_a_clone_of_the_supplied_document()
    {
        FlowValue value;
        using (var document = JsonDocument.Parse("{\"enabled\":true}"))
            value = FlowValue.FromJson(document.RootElement);

        value.Kind.ShouldBe(JsonValueKind.Object);
        value.ToJsonElement().GetProperty("enabled").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public void Default_json_projection_reuses_owned_storage_after_source_disposal()
    {
        FlowValue value;
        using (var document = JsonDocument.Parse("""{"items":["alpha",42,null],"enabled":true}"""))
            value = FlowValue.FromJson(document.RootElement);

        var projected = value.Deserialize<JsonElement>();

        projected.ShouldBe(value.ToJsonElement());
        value.Deserialize<JsonElement>().ShouldBe(projected);
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
        var value = FlowValue.From(new { Enabled = true });

        var configured = value.Deserialize<JsonElement>(options);

        configured.GetString().ShouldBe("configured");
        converter.ReadCount.ShouldBe(1);
        value.Deserialize<JsonElement>().GetProperty("enabled").GetBoolean().ShouldBeTrue();
        converter.ReadCount.ShouldBe(1);
    }

    [Fact]
    public void From_preserves_an_existing_flow_value()
    {
        var value = FlowValue.From("payload");

        FlowValue.From(value).ShouldBeSameAs(value);
    }

    [Fact]
    public void Convert_value_changes_only_the_payload_representation()
    {
        var original = FlowMessage.Create(
            new SamplePayload("alpha", 7),
            correlationId: CorrelationId.New(),
            headers: new Dictionary<string, string> { ["source"] = "test" });

        var converted = original.ConvertValue(FlowValue.From(original.Value));

        converted.Value!.Deserialize<SamplePayload>().ShouldBe(original.Value);
        converted.CorrelationId.ShouldBe(original.CorrelationId);
        converted.TraceId.ShouldBe(original.TraceId);
        converted.MessageId.ShouldBe(original.MessageId);
        converted.CausationId.ShouldBe(original.CausationId);
        converted.Timestamp.ShouldBe(original.Timestamp);
        converted.Headers.ShouldBe(original.Headers);
    }

    [Fact]
    public void From_omits_undefined_json_element_properties()
    {
        var value = FlowValue.From(new { Optional = default(JsonElement) });

        value.ToJsonElement().TryGetProperty("optional", out _).ShouldBeFalse();
    }

    [Fact]
    public void From_uses_the_concrete_runtime_type_for_polymorphic_values()
    {
        PolymorphicPayload payload = new ConcretePayload("runtime");

        var value = FlowValue.From(payload);

        value.ToJsonElement().GetProperty("name").GetString().ShouldBe("runtime");
    }

    [Fact]
    public void Primitive_materializers_accept_only_their_owned_shapes()
    {
        var materialized = FlowValueMaterializers.String.Materialize(FlowValue.From("value"));
        var rejected = FlowValueMaterializers.String.Materialize(FlowValue.From(42));

        materialized.IsSuccess.ShouldBeTrue();
        materialized.Value.ShouldBe("value");
        rejected.IsSuccess.ShouldBeFalse();
        rejected.Error.ShouldNotBeNull().Code.ShouldBe("flow.value.expected_string");
    }

    [Fact]
    public void Primitive_materializer_lookup_rejects_domain_types()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => FlowValueMaterializers.Primitive<SamplePayload>());

        exception.Message.ShouldContain("owning module must provide");
    }

    [Fact]
    public void Input_shape_validates_json_kind_and_required_properties_case_insensitively()
    {
        var shape = FlowValueShape.Object(
            "An HTTP-like request.",
            "method",
            "url");

        shape.Validate(FlowValue.From(new { Method = "POST", URL = "/orders" }))
            .IsValid.ShouldBeTrue();

        var missing = shape.Validate(FlowValue.From(new { method = "POST" }));
        missing.IsValid.ShouldBeFalse();
        missing.Error.ShouldNotBeNull().ShouldContain("url");

        var wrongKind = shape.Validate(FlowValue.From("POST /orders"));
        wrongKind.IsValid.ShouldBeFalse();
        wrongKind.Error.ShouldNotBeNull().ShouldContain(nameof(JsonValueKind.String));
    }

    [Fact]
    public void Input_shape_equality_compares_kind_and_property_sets_and_description()
    {
        var first = new FlowValueShape("Request", [JsonValueKind.Object, JsonValueKind.Null], ["id", "value"]);
        var reordered = new FlowValueShape("Request", [JsonValueKind.Null, JsonValueKind.Object], ["VALUE", "ID"]);

        first.Equals(reordered).ShouldBeTrue();
        first.GetHashCode().ShouldBe(reordered.GetHashCode());
        first.Equals(new FlowValueShape("Request", [JsonValueKind.Object], ["id", "value"])).ShouldBeFalse();
        first.Equals(new FlowValueShape("Request", first.AcceptedKinds, ["id"])).ShouldBeFalse();
        first.Equals(new FlowValueShape("Other request", first.AcceptedKinds, first.RequiredProperties)).ShouldBeFalse();
        first.Equals(null).ShouldBeFalse();
    }

    [Fact]
    public void Input_shape_round_trip_preserves_nullable_object_validation_and_snapshot()
    {
        JsonValueKind[] kinds = [JsonValueKind.Object, JsonValueKind.Null];
        string[] properties = ["id"];
        var original = new FlowValueShape("Optional request", kinds, properties);
        kinds[0] = JsonValueKind.String;
        properties[0] = "changed";
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var roundTripped = JsonSerializer.Deserialize<FlowValueShape>(
            JsonSerializer.Serialize(original, options), options).ShouldNotBeNull();

        roundTripped.Equals(original).ShouldBeTrue();
        roundTripped.Validate(FlowValue.Null).IsValid.ShouldBeTrue();
        roundTripped.Validate(FlowValue.From(new { ID = "42" })).IsValid.ShouldBeTrue();
        roundTripped.Validate(FlowValue.From(new { changed = "42" })).IsValid.ShouldBeFalse();
        roundTripped.Validate(FlowValue.From("42")).IsValid.ShouldBeFalse();
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

        var value = FlowValue.From(source);
        tags[0] = "mutated";
        tags.Add("later");
        source["optional"] = "changed";

        var json = value.ToJsonElement();
        json.GetProperty("nested").GetProperty("tags").EnumerateArray()
            .Select(item => item.GetString()).ShouldBe(["original", "second"]);
        json.GetProperty("optional").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public void Record_copy_preserves_owned_storage_after_document_disposal()
    {
        FlowValue original;
        FlowValue copy;
        using (var document = JsonDocument.Parse("""{"items":["alpha",42,null],"enabled":true}"""))
        {
            original = FlowValue.FromJson(document.RootElement);
            copy = original with { };
        }

        copy.ShouldNotBeSameAs(original);
        (copy == original).ShouldBeTrue();
        copy.GetHashCode().ShouldBe(original.GetHashCode());
        var json = copy.ToJsonElement();
        json.GetProperty("items")[0].GetString().ShouldBe("alpha");
        json.GetProperty("items")[1].GetInt32().ShouldBe(42);
        json.GetProperty("items")[2].ValueKind.ShouldBe(JsonValueKind.Null);
        json.GetProperty("enabled").GetBoolean().ShouldBeTrue();
        FlowValue.From(copy).ShouldBeSameAs(copy);
    }

    [Fact]
    public void Independently_owned_equal_json_does_not_imply_record_equality()
    {
        var first = FlowValue.From(new { Value = 42 });
        var second = FlowValue.From(new { Value = 42 });

        first.ToString().ShouldBe("{\"value\":42}");
        second.ToString().ShouldBe("{\"value\":42}");
        (first == second).ShouldBeFalse();
        first.Equals(null).ShouldBeFalse();
        var sharedStorage = FlowValue.FromJson(first.ToJsonElement());
        sharedStorage.ShouldNotBeSameAs(first);
        sharedStorage.Equals(first).ShouldBeTrue();
        sharedStorage.GetHashCode().ShouldBe(first.GetHashCode());
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
