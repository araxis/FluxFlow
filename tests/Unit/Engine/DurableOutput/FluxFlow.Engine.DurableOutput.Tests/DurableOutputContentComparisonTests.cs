using System.Text.Json;
using FluxFlow.Composition.Addressing;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.Tests;

public sealed class DurableOutputContentComparisonTests
{
    public static TheoryData<DurableOutputContentMutation> ContentMutations =>
        DurableOutputStoreConformanceTests.ConflictMutations;

    [Fact]
    public void Equivalent_content_ignores_capture_time_object_order_and_header_order()
    {
        var original = DurableOutputStoreConformanceData.Envelope(
            payload: Json("""
                {
                  "id": 42,
                  "customer": { "name": "Ada", "tier": 3 },
                  "lines": [ { "sku": "A-1", "quantity": 2 }, true, null ]
                }
                """),
            headers: new Dictionary<string, string>
            {
                ["source"] = "orders",
                ["tenant"] = "north"
            });
        var equivalent = DurableOutputStoreConformanceData.Copy(
            original,
            payload: Json("""
                {
                  "lines": [ { "quantity": 2, "sku": "A-1" }, true, null ],
                  "customer": { "tier": 3, "name": "Ada" },
                  "id": 42
                }
                """),
            capturedAt: original.CapturedAt.AddDays(1),
            headers: new Dictionary<string, string>
            {
                ["tenant"] = "north",
                ["source"] = "orders"
            });

        original.HasSameContent(equivalent).ShouldBeTrue();
        equivalent.HasSameContent(original).ShouldBeTrue();
        equivalent.CapturedAt.ShouldNotBe(original.CapturedAt);
        equivalent.Payload.GetRawText().ShouldNotBe(original.Payload.GetRawText());
    }

    [Theory]
    [MemberData(nameof(ContentMutations))]
    public void Meaningful_same_key_mutation_is_not_equivalent(
        DurableOutputContentMutation mutation)
    {
        var original = DurableOutputStoreConformanceData.Envelope();
        var changed = DurableOutputStoreConformanceData.MutateSameKey(original, mutation);

        original.Key.ShouldBe(changed.Key);
        original.HasSameContent(changed).ShouldBeFalse();
        changed.HasSameContent(original).ShouldBeFalse();
    }

    [Fact]
    public void Address_and_message_id_are_content_identity()
    {
        var original = DurableOutputStoreConformanceData.Envelope();
        var changedAddress = DurableOutputStoreConformanceData.Copy(
            original,
            address: DurableOutputStoreConformanceData.SecondaryOutput);
        var changedMessage = DurableOutputStoreConformanceData.Copy(
            original,
            messageId: new MessageId("message-2"));

        original.HasSameContent(changedAddress).ShouldBeFalse();
        original.HasSameContent(changedMessage).ShouldBeFalse();
        original.Key.ShouldNotBe(changedAddress.Key);
        original.Key.ShouldNotBe(changedMessage.Key);
    }

    [Fact]
    public void Json_array_order_and_numeric_representation_are_meaningful()
    {
        var original = DurableOutputStoreConformanceData.Envelope(
            payload: Json("{\"values\":[1,2,3],\"number\":1}"));
        var reorderedArray = DurableOutputStoreConformanceData.Copy(
            original,
            payload: Json("{\"values\":[3,2,1],\"number\":1}"));
        var changedNumberRepresentation = DurableOutputStoreConformanceData.Copy(
            original,
            payload: Json("{\"values\":[1,2,3],\"number\":1.0}"));

        original.HasSameContent(reorderedArray).ShouldBeFalse();
        original.HasSameContent(changedNumberRepresentation).ShouldBeFalse();
    }

    [Fact]
    public void Error_content_compares_every_field_and_semantic_details()
    {
        var original = DurableOutputStoreConformanceData.ErrorEnvelope();
        var equivalent = DurableOutputStoreConformanceData.ErrorEnvelope(
            capturedAt: original.CapturedAt.AddHours(1),
            error: DurableOutputStoreConformanceData.Error(
                details: Json("""
                    {
                      "violations": ["required", "known-customer"],
                      "field": "customerId"
                    }
                    """)));
        var mutations = new[]
        {
            DurableOutputStoreConformanceData.Error(code: "order.changed"),
            DurableOutputStoreConformanceData.Error(message: "Changed message."),
            DurableOutputStoreConformanceData.Error(category: "changed"),
            DurableOutputStoreConformanceData.Error(isTransient: true),
            DurableOutputStoreConformanceData.Error(details: Json("{\"field\":\"other\"}"))
        };

        original.HasSameContent(equivalent).ShouldBeTrue();
        foreach (var error in mutations)
        {
            original.HasSameContent(
                DurableOutputStoreConformanceData.ErrorEnvelope(error: error)).ShouldBeFalse();
        }
    }

    [Fact]
    public void Header_names_and_values_are_ordinal_and_exact()
    {
        var original = DurableOutputStoreConformanceData.Envelope(
            headers: new Dictionary<string, string> { ["Tenant"] = "North" });
        var changedName = DurableOutputStoreConformanceData.Copy(
            original,
            headers: new Dictionary<string, string> { ["tenant"] = "North" });
        var changedValue = DurableOutputStoreConformanceData.Copy(
            original,
            headers: new Dictionary<string, string> { ["Tenant"] = "north" });
        var addedHeader = DurableOutputStoreConformanceData.Copy(
            original,
            headers: new Dictionary<string, string>
            {
                ["Tenant"] = "North",
                ["extra"] = "value"
            });

        original.HasSameContent(changedName).ShouldBeFalse();
        original.HasSameContent(changedValue).ShouldBeFalse();
        original.HasSameContent(addedHeader).ShouldBeFalse();
    }

    [Fact]
    public void Null_peer_is_rejected()
    {
        var exception = Should.Throw<ArgumentNullException>(() =>
            DurableOutputStoreConformanceData.Envelope().HasSameContent(null!));

        exception.ParamName.ShouldBe("other");
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
