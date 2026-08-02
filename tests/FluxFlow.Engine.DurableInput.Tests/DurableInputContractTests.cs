using System.Text.Json;
using FluxFlow.Engine.DurableInput;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.Tests;

public sealed class DurableInputContractTests
{
    [Fact]
    public void Envelope_owns_payload_and_headers_and_uses_stable_identity()
    {
        using var document = JsonDocument.Parse("""{"value":42}""");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Source"] = "orders"
        };

        var envelope = new DurableInputEnvelope(
            DurableInputTestData.Input,
            "order-submitted-v1",
            isError: false,
            document.RootElement,
            error: null,
            new FluxFlow.Nodes.MessageId("message-42"),
            new FluxFlow.Nodes.TraceId("trace-42"),
            DurableInputTestData.Now,
            DurableInputTestData.Now,
            headers: headers);
        headers["Source"] = "changed";
        headers["later"] = "ignored";
        document.Dispose();

        envelope.Payload.GetProperty("value").GetInt32().ShouldBe(42);
        envelope.Headers.ShouldBe(new Dictionary<string, string> { ["Source"] = "orders" });
        envelope.Headers.ContainsKey("source").ShouldBeFalse();
        envelope.Headers.ContainsKey("later").ShouldBeFalse();
        envelope.ContractName.ShouldBe("order-submitted-v1");
        envelope.ContractName.ShouldNotContain(typeof(string).AssemblyQualifiedName!);
        envelope.Key.ShouldBe(new DurableInputKey(envelope.Address, envelope.MessageId));
    }

    [Fact]
    public void HasSameContent_ignores_enqueue_time_but_detects_payload_conflicts()
    {
        var first = DurableInputTestData.Envelope(enqueuedAt: DurableInputTestData.Now);
        var duplicate = DurableInputTestData.Envelope(enqueuedAt: DurableInputTestData.Now.AddMinutes(1));
        var conflict = DurableInputTestData.Envelope(
            value: "changed",
            enqueuedAt: DurableInputTestData.Now.AddMinutes(1));

        first.HasSameContent(duplicate).ShouldBeTrue();
        first.HasSameContent(conflict).ShouldBeFalse();
    }

    [Fact]
    public void HasSameContent_treats_reordered_json_object_properties_as_equivalent()
    {
        using var firstJson = JsonDocument.Parse(
            """{"orderId":"42","customer":{"name":"Ada","priority":true}}""");
        using var reorderedJson = JsonDocument.Parse(
            """{"customer":{"priority":true,"name":"Ada"},"orderId":"42"}""");

        var first = EnvelopeWithPayload(firstJson.RootElement, DurableInputTestData.Now);
        var reordered = EnvelopeWithPayload(
            reorderedJson.RootElement,
            DurableInputTestData.Now.AddMinutes(1));

        first.HasSameContent(reordered).ShouldBeTrue();
    }

    [Fact]
    public void Registry_rejects_duplicate_stable_names_and_payload_types()
    {
        var duplicateName = Should.Throw<InvalidOperationException>(() =>
            new DurableInputContractRegistry(
            [
                new DurableInputContract<string>("contract-v1", jsonTypeInfo: null),
                new DurableInputContract<int>("contract-v1", jsonTypeInfo: null)
            ]));
        var duplicateType = Should.Throw<InvalidOperationException>(() =>
            new DurableInputContractRegistry(
            [
                new DurableInputContract<string>("contract-v1", jsonTypeInfo: null),
                new DurableInputContract<string>("contract-v2", jsonTypeInfo: null)
            ]));

        duplicateName.Message.ShouldContain("contract-v1");
        duplicateType.Message.ShouldContain(typeof(string).ToString());
    }

    [Fact]
    public void Registry_uses_exact_ordinal_names_and_rejects_surrounding_whitespace()
    {
        var contract = new DurableInputContract<string>("contract-v1", jsonTypeInfo: null);
        var registry = new DurableInputContractRegistry([contract]);

        registry.TryGetByName("contract-v1", out var resolved).ShouldBeTrue();
        resolved.ShouldBeSameAs(contract);
        registry.TryGetByName("Contract-v1", out var caseChanged).ShouldBeFalse();
        caseChanged.ShouldBeNull();
        Should.Throw<ArgumentException>(() =>
                new DurableInputContract<string>(" contract-v1 ", jsonTypeInfo: null))
            .ParamName.ShouldBe("name");
    }

    [Fact]
    public void Lease_and_transition_validation_rejects_ambiguous_provider_commands()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new DurableInputLeaseRequest("owner", DurableInputTestData.Now, DurableInputTestData.Now, 1));
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new DurableInputLeaseRequest(
                "owner",
                DurableInputTestData.Now,
                DurableInputTestData.Now.AddMinutes(1),
                0));
        Should.Throw<ArgumentException>(() =>
            new DurableInputLease(
                DurableInputTestData.Envelope(),
                Guid.Empty,
                "owner",
                DurableInputTestData.Now,
                DurableInputTestData.Now.AddMinutes(1),
                1));
    }

    private static DurableInputEnvelope EnvelopeWithPayload(
        JsonElement payload,
        DateTimeOffset enqueuedAt)
        => new(
            DurableInputTestData.Input,
            "order-v1",
            isError: false,
            payload,
            error: null,
            new FluxFlow.Nodes.MessageId("message-semantic"),
            new FluxFlow.Nodes.TraceId("trace-semantic"),
            DurableInputTestData.Now.AddMinutes(-1),
            enqueuedAt,
            headers: new Dictionary<string, string> { ["source"] = "test" });
}
