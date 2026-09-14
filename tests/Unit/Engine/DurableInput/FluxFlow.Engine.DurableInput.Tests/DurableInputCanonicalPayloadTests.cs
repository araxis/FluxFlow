using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using FluxFlow.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.Tests;

public sealed class DurableInputCanonicalPayloadTests
{
    [Theory]
    [InlineData(false, "OrderId")]
    [InlineData(true, "ORDER_ID")]
    public async Task Restored_payload_keeps_contract_property_names(bool customNaming, string propertyName)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseUpper,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
        var contract = new DurableInputContract<DurableWireOrder>("order-v1",
            customNaming ? (JsonTypeInfo<DurableWireOrder>)options.GetTypeInfo(typeof(DurableWireOrder)) : null);
        var envelope = Envelope(contract);

        envelope.Payload.GetProperty(propertyName).GetInt32().ShouldBe(73);
        await AssertRestoredAsync(contract, envelope);
    }

    [Fact]
    public async Task Restored_payload_keeps_generated_metadata_shape()
    {
        var contract = new DurableInputContract<DurableWireOrder>("order-v1",
            DurableWireJsonContext.Default.DurableWireOrder);
        var envelope = Envelope(contract);

        envelope.Payload.GetProperty("ORDER_ID").GetInt32().ShouldBe(73);
        await AssertRestoredAsync(contract, envelope);
    }

    [Fact]
    public async Task Restored_payload_keeps_fields_not_materialized_by_the_contract()
    {
        var contract = new DurableInputContract<DurableWireOrder>("order-v1", jsonTypeInfo: null);
        using var document = JsonDocument.Parse("""{"OrderId":73,"extension":{"version":2}}""");
        var envelope = WithPayload(Envelope(contract), document.RootElement);
        document.Dispose();

        await AssertRestoredAsync(contract, envelope);
    }

    [Fact]
    public async Task Restored_payload_validates_custom_converter_without_reserializing_it()
    {
        var converter = new WireOrderConverter();
        var contract = ConverterContract(converter);
        var envelope = Envelope(contract);

        envelope.Payload.GetString().ShouldBe("order:73");
        await AssertRestoredAsync(contract, envelope);
        converter.Reads.ShouldBe(1);
        converter.Writes.ShouldBe(1);
    }

    [Fact]
    public async Task Invalid_custom_payload_is_dead_lettered_without_workflow_delivery()
    {
        await using var host = await DurableInputTestApplication.CreateAsync();
        var converter = new WireOrderConverter();
        var contract = ConverterContract(converter);
        var envelope = WithPayload(Envelope(contract), JsonSerializer.SerializeToElement("invalid"));
        var store = new DurableInputTestStore();
        await store.EnqueueAsync(envelope);

        (await Dispatcher(store, host, contract).ProcessOnceAsync()).ShouldBeTrue();

        store.Get(envelope.Key).State.ShouldBe(DurableInputState.DeadLettered);
        store.DeadLetters.ShouldHaveSingleItem().Failure.Kind
            .ShouldBe(DurableInputFailureKind.DeserializationFailed);
        host.Recorder.Messages.ShouldBeEmpty();
        store.DeliveredTransitions.ShouldBeEmpty();
        converter.Reads.ShouldBe(1);
        converter.Writes.ShouldBe(1);
    }

    private static async Task AssertRestoredAsync(IDurableInputContract contract, DurableInputEnvelope envelope)
    {
        await using var host = await DurableInputTestApplication.CreateAsync();
        var store = new DurableInputTestStore();
        await store.EnqueueAsync(envelope);

        (await Dispatcher(store, host, contract).ProcessOnceAsync()).ShouldBeTrue();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var received = (await host.Recorder.WaitAsync(timeout.Token)).Message;

        received.IsError.ShouldBeFalse();
        received.Value.Json.GetRawText().ShouldBe(envelope.Payload.GetRawText());
        received.MessageId.ShouldBe(envelope.MessageId);
        received.TraceId.ShouldBe(envelope.TraceId);
        received.Timestamp.ShouldBe(envelope.Timestamp);
        received.CorrelationId.ShouldBe(envelope.CorrelationId);
        received.CausationId.ShouldBe(envelope.CausationId);
        received.Headers.ShouldBe(envelope.Headers);
        store.Get(envelope.Key).State.ShouldBe(DurableInputState.Delivered);
        store.DeliveredTransitions.ShouldHaveSingleItem().Key.ShouldBe(envelope.Key);
        store.DeadLetters.ShouldBeEmpty();
    }

    private static DurableInputEnvelope Envelope(DurableInputContract<DurableWireOrder> contract)
        => contract.CreateEnvelope(DurableInputTestData.Input,
            FlowMessage.Restore(new DurableWireOrder(73), new MessageId("wire-message"),
                new TraceId("wire-trace"), DurableInputTestData.Now.AddMinutes(-1),
                new CorrelationId("wire-exchange"), new MessageId("wire-cause"),
                new Dictionary<string, string> { ["source"] = "order" }), DurableInputTestData.Now);

    private static DurableInputEnvelope WithPayload(DurableInputEnvelope envelope, JsonElement payload)
        => new(envelope.Address, envelope.ContractName, false, payload, null,
            envelope.MessageId, envelope.TraceId, envelope.Timestamp, envelope.EnqueuedAt,
            envelope.CorrelationId, envelope.CausationId, envelope.Headers);

    private static DurableInputDispatcher Dispatcher(DurableInputTestStore store,
        DurableInputTestApplication host, IDurableInputContract contract)
        => new(store, new DurableInputContractRegistry([contract]), host.Application,
            DurableInputOptions.Default, new FakeTimeProvider(DurableInputTestData.Now),
            NullLogger<DurableInputDispatcher>.Instance);

    private static DurableInputContract<DurableWireOrder> ConverterContract(WireOrderConverter converter)
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            Converters = { converter }
        };
        return new DurableInputContract<DurableWireOrder>("order-v1",
            (JsonTypeInfo<DurableWireOrder>)options.GetTypeInfo(typeof(DurableWireOrder)));
    }

    private sealed class WireOrderConverter : JsonConverter<DurableWireOrder>
    {
        public int Reads { get; private set; }
        public int Writes { get; private set; }

        public override DurableWireOrder Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options)
        {
            Reads++;
            var value = reader.GetString();
            if (value is null || !value.StartsWith("order:", StringComparison.Ordinal) ||
                !int.TryParse(value.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                throw new JsonException("Expected an order identifier.");
            return new DurableWireOrder(id);
        }

        public override void Write(Utf8JsonWriter writer, DurableWireOrder value, JsonSerializerOptions options)
        {
            Writes++;
            writer.WriteStringValue($"order:{value.OrderId.ToString(CultureInfo.InvariantCulture)}");
        }
    }
}

internal sealed record DurableWireOrder(int OrderId);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseUpper)]
[JsonSerializable(typeof(DurableWireOrder))]
internal partial class DurableWireJsonContext : JsonSerializerContext;
