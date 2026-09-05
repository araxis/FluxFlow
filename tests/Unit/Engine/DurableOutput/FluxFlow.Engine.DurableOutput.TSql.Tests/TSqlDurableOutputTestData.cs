using System.Text.Json;
using FluxFlow.Composition.Addressing;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.DurableOutput.TSql.Tests;

internal static class TSqlDurableOutputTestData
{
    internal const string UnreachableConnectionString =
        "Server=127.0.0.1,1;Database=FluxFlowNoIo;User ID=no-io;Password=not-used;" +
        "Encrypt=False;TrustServerCertificate=True;Connect Timeout=1";

    internal static readonly DateTimeOffset Now =
        new(2026, 8, 1, 13, 0, 0, TimeSpan.FromHours(2));

    internal static DurableOutputEnvelope Envelope(
        string messageId = "message-1",
        ApplicationAddress? address = null,
        string contractName = "order-v1",
        string traceId = "trace-1",
        string? correlationId = "correlation-1",
        string? causationId = "cause-1",
        FlowError? error = null)
        => new(
            address ?? ApplicationAddress.WorkflowPort("Orders", "Publisher", "Output"),
            contractName,
            isError: error is not null,
            error is null
                ? JsonSerializer.SerializeToElement(new { id = 42, customer = "Ada" })
                : JsonSerializer.SerializeToElement<object?>(null),
            error,
            new MessageId(messageId),
            new TraceId(traceId),
            Now,
            Now.AddSeconds(1),
            correlationId is null ? null : new CorrelationId(correlationId),
            causationId is null ? null : new MessageId(causationId),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["source"] = "fast-tests"
            },
            DurableOutputEnvelope.CurrentSchemaVersion);

    internal static FlowError Error(
        string code = "order.invalid",
        string category = "validation")
        => new(
            code,
            "The order is invalid.",
            category,
            isTransient: false,
            JsonSerializer.SerializeToElement(new { field = "customer" }));
}
