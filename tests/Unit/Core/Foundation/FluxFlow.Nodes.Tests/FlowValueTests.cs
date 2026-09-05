using System.Text.Json;
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

    private sealed record SamplePayload(string Name, int Count);

    private abstract record PolymorphicPayload;

    private sealed record ConcretePayload(string Name) : PolymorphicPayload;
}
