using System.Text.Json;
using FluxFlow.Composition.Addressing;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.DurableInput.Tests;

internal static class DurableInputTestData
{
    public static readonly ApplicationAddress Input =
        ApplicationAddress.WorkflowPort("Orders", "Handler", "Input");

    public static readonly DateTimeOffset Now =
        new(2026, 7, 29, 10, 0, 0, TimeSpan.Zero);

    public static DurableInputEnvelope Envelope(
        string value = "payload",
        DateTimeOffset? enqueuedAt = null,
        string contractName = "text-v1",
        MessageId? messageId = null,
        int schemaVersion = DurableInputEnvelope.CurrentSchemaVersion)
        => new(
            Input,
            contractName,
            isError: false,
            JsonSerializer.SerializeToElement(value),
            error: null,
            messageId ?? new MessageId("message-1"),
            new TraceId("trace-1"),
            new DateTimeOffset(2026, 7, 29, 9, 59, 0, TimeSpan.Zero),
            enqueuedAt ?? Now,
            new CorrelationId("order-1"),
            new MessageId("cause-1"),
            new Dictionary<string, string> { ["source"] = "test" },
            schemaVersion);
}
