using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using FluxFlow.Composition.Addressing;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.DurableOutput.Tests;

internal static class DurableOutputTestData
{
    internal static readonly ApplicationAddress Output =
        ApplicationAddress.WorkflowPort("Orders", "Publisher", "Output");

    internal static readonly ApplicationAddress SecondOutput =
        ApplicationAddress.WorkflowPort("Orders", "Audit", "Output");

    internal static readonly DateTimeOffset MessageTimestamp =
        new(2026, 7, 30, 10, 0, 0, TimeSpan.Zero);

    internal static readonly DateTimeOffset CapturedAt =
        new(2026, 7, 30, 10, 0, 1, TimeSpan.Zero);

    internal static JsonTypeInfo<T> TypeInfo<T>()
        => (JsonTypeInfo<T>)JsonSerializerOptions.Default.GetTypeInfo(typeof(T));

    internal static DurableOutputEnvelope Envelope(
        JsonElement? payload = null,
        string contractName = "text-v1",
        ApplicationAddress? address = null,
        MessageId? messageId = null,
        TraceId? traceId = null,
        IReadOnlyDictionary<string, string>? headers = null,
        int schemaVersion = DurableOutputEnvelope.CurrentSchemaVersion)
        => new(
            address ?? Output,
            contractName,
            isError: false,
            payload ?? JsonSerializer.SerializeToElement("payload"),
            error: null,
            messageId ?? new MessageId("message-1"),
            traceId ?? new TraceId("trace-1"),
            MessageTimestamp,
            CapturedAt,
            new CorrelationId("order-1"),
            new MessageId("cause-1"),
            headers ?? new Dictionary<string, string> { ["source"] = "test" },
            schemaVersion);

    internal static FlowError Error()
        => new(
            "order.invalid",
            "The order is invalid.",
            "validation",
            isTransient: false,
            details: JsonSerializer.SerializeToElement(new { field = "customerId" }));
}
