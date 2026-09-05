using System.Text.Json;
using FluxFlow.Composition.Addressing;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.DurableOutput.Tests;

/// <summary>
/// Deterministic provider-neutral values shared by durable-output store tests.
/// </summary>
public static class DurableOutputStoreConformanceData
{
    public static readonly ApplicationAddress Output =
        ApplicationAddress.WorkflowPort("Orders", "Publisher", "Output");

    public static readonly ApplicationAddress SecondaryOutput =
        ApplicationAddress.WorkflowPort("Orders", "Audit", "Output");

    public static readonly DateTimeOffset MessageTimestamp =
        new(2026, 7, 30, 10, 15, 30, TimeSpan.FromHours(2));

    public static readonly DateTimeOffset CapturedAt =
        new(2026, 7, 30, 8, 15, 31, TimeSpan.Zero);

    public static readonly DateTimeOffset DeliveryNow =
        new(2026, 8, 1, 11, 0, 0, TimeSpan.FromHours(2));

    public static DurableOutputDeliveryLeaseRequest DeliveryRequest(
        DateTimeOffset now,
        string ownerId = "worker-1",
        TimeSpan? leaseDuration = null)
        => new(ownerId, now, now.Add(leaseDuration ?? TimeSpan.FromSeconds(30)));

    public static DurableOutputDeliveryDeadLetter DeadLetter(
        DurableOutputKey key,
        Guid leaseToken,
        DateTimeOffset deadLetteredAt)
        => new(
            key,
            leaseToken,
            deadLetteredAt,
            DurableOutputDeadLetterReason.HandlerFailure);

    public static DurableOutputEnvelope Envelope(
        string messageId = "message-1",
        ApplicationAddress? address = null,
        string contractName = "order-v1",
        JsonElement? payload = null,
        string traceId = "trace-1",
        DateTimeOffset? timestamp = null,
        DateTimeOffset? capturedAt = null,
        string? correlationId = "order-1",
        string? causationId = "cause-1",
        IReadOnlyDictionary<string, string>? headers = null,
        int schemaVersion = DurableOutputEnvelope.CurrentSchemaVersion)
        => new(
            address ?? Output,
            contractName,
            isError: false,
            payload ?? JsonSerializer.SerializeToElement(new
            {
                id = 42,
                customer = "Ada",
                lines = new[]
                {
                    new { sku = "A-1", quantity = 2 },
                    new { sku = "B-2", quantity = 1 }
                }
            }),
            error: null,
            new MessageId(messageId),
            new TraceId(traceId),
            timestamp ?? MessageTimestamp,
            capturedAt ?? CapturedAt,
            correlationId is null ? null : new CorrelationId(correlationId),
            causationId is null ? null : new MessageId(causationId),
            headers ?? new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["source"] = "orders",
                ["tenant"] = "north"
            },
            schemaVersion);

    public static DurableOutputEnvelope ErrorEnvelope(
        string messageId = "error-message-1",
        DateTimeOffset? capturedAt = null,
        FlowError? error = null)
        => new(
            Output,
            "order-error-v1",
            isError: true,
            JsonSerializer.SerializeToElement<object?>(null),
            error ?? Error(),
            new MessageId(messageId),
            new TraceId("error-trace-1"),
            MessageTimestamp,
            capturedAt ?? CapturedAt,
            new CorrelationId("error-order-1"),
            new MessageId("error-cause-1"),
            new Dictionary<string, string> { ["source"] = "validation" },
            DurableOutputEnvelope.CurrentSchemaVersion);

    public static FlowError Error(
        string code = "order.invalid",
        string message = "The order is invalid.",
        string category = "validation",
        bool isTransient = false,
        JsonElement? details = null)
        => new(
            code,
            message,
            category,
            isTransient,
            details ?? JsonSerializer.SerializeToElement(new
            {
                field = "customerId",
                violations = new[] { "required", "known-customer" }
            }));

    public static DurableOutputEnvelope MutateSameKey(
        DurableOutputEnvelope original,
        DurableOutputContentMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(original);
        return mutation switch
        {
            DurableOutputContentMutation.ContractName => Copy(
                original,
                contractName: original.ContractName + "-changed"),
            DurableOutputContentMutation.ValueOrErrorCase => ErrorEnvelope(
                original.MessageId.Value,
                original.CapturedAt),
            DurableOutputContentMutation.Payload => Copy(
                original,
                payload: JsonSerializer.SerializeToElement(new
                {
                    id = 43,
                    customer = "Ada",
                    lines = Array.Empty<object>()
                })),
            DurableOutputContentMutation.TraceId => Copy(
                original,
                traceId: new TraceId("trace-changed")),
            DurableOutputContentMutation.Timestamp => Copy(
                original,
                timestamp: original.Timestamp.AddTicks(1)),
            DurableOutputContentMutation.CorrelationId => Copy(
                original,
                correlationId: new CorrelationId("order-changed")),
            DurableOutputContentMutation.CausationId => Copy(
                original,
                causationId: new MessageId("cause-changed")),
            DurableOutputContentMutation.Headers => Copy(
                original,
                headers: new Dictionary<string, string>
                {
                    ["source"] = "changed",
                    ["tenant"] = "north"
                }),
            DurableOutputContentMutation.SchemaVersion => Copy(
                original,
                schemaVersion: original.SchemaVersion + 1),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
    }

    public static DurableOutputEnvelope Copy(
        DurableOutputEnvelope original,
        ApplicationAddress? address = null,
        string? contractName = null,
        JsonElement? payload = null,
        FlowError? error = null,
        bool? isError = null,
        MessageId? messageId = null,
        TraceId? traceId = null,
        DateTimeOffset? timestamp = null,
        DateTimeOffset? capturedAt = null,
        CorrelationId? correlationId = null,
        MessageId? causationId = null,
        IReadOnlyDictionary<string, string>? headers = null,
        int? schemaVersion = null)
    {
        ArgumentNullException.ThrowIfNull(original);
        var errorCase = isError ?? original.IsError;
        return new DurableOutputEnvelope(
            address ?? original.Address,
            contractName ?? original.ContractName,
            errorCase,
            payload ?? original.Payload,
            errorCase ? error ?? original.Error : null,
            messageId ?? original.MessageId,
            traceId ?? original.TraceId,
            timestamp ?? original.Timestamp,
            capturedAt ?? original.CapturedAt,
            correlationId ?? original.CorrelationId,
            causationId ?? original.CausationId,
            headers ?? original.Headers,
            schemaVersion ?? original.SchemaVersion);
    }
}

public enum DurableOutputContentMutation
{
    ContractName,
    ValueOrErrorCase,
    Payload,
    TraceId,
    Timestamp,
    CorrelationId,
    CausationId,
    Headers,
    SchemaVersion
}
