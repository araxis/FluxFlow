using System.Text.Json;
using FluxFlow.Composition.Addressing;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.DurableInput.Tests;

/// <summary>
/// Deterministic provider-neutral values shared by durable-input store tests.
/// </summary>
public static class DurableInputStoreConformanceData
{
    public static readonly ApplicationAddress Input =
        ApplicationAddress.WorkflowPort("Orders", "Handler", "Input");

    public static readonly ApplicationAddress SecondaryInput =
        ApplicationAddress.WorkflowPort("Orders", "Secondary", "Input");

    public static readonly DateTimeOffset Now =
        new(2026, 7, 29, 10, 0, 0, TimeSpan.Zero);

    public static DurableInputEnvelope Envelope(
        string messageId = "message-1",
        string value = "payload",
        ApplicationAddress? address = null,
        DateTimeOffset? timestamp = null,
        DateTimeOffset? enqueuedAt = null,
        string contractName = "text-v1",
        string traceId = "trace-1",
        string? correlationId = "order-1",
        string? causationId = "cause-1",
        IReadOnlyDictionary<string, string>? headers = null,
        int schemaVersion = DurableInputEnvelope.CurrentSchemaVersion)
        => new(
            address ?? Input,
            contractName,
            isError: false,
            JsonSerializer.SerializeToElement(value),
            error: null,
            new MessageId(messageId),
            new TraceId(traceId),
            timestamp ?? Now.AddMinutes(-1),
            enqueuedAt ?? Now,
            correlationId is null ? null : new CorrelationId(correlationId),
            causationId is null ? null : new MessageId(causationId),
            headers ?? new Dictionary<string, string> { ["source"] = "test" },
            schemaVersion);

    public static DurableInputFailure Failure(
        DurableInputFailureKind kind = DurableInputFailureKind.InputUnavailable,
        string? description = null)
        => new(kind, description ?? kind.ToString());

    public static DurableInputLeaseRequest Request(
        string ownerId = "owner",
        DateTimeOffset? now = null,
        DateTimeOffset? leaseUntil = null,
        int maxCount = 1)
    {
        var requestedAt = now ?? Now;
        return new DurableInputLeaseRequest(
            ownerId,
            requestedAt,
            leaseUntil ?? requestedAt.AddSeconds(30),
            maxCount);
    }
}
