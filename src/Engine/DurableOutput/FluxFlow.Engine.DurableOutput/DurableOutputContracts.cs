using System.Collections.Immutable;
using System.Text.Json;
using FluxFlow.Composition.Addressing;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.DurableOutput;

public readonly record struct DurableOutputKey
{
    public DurableOutputKey(ApplicationAddress address, MessageId messageId)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (messageId.IsEmpty)
            throw new ArgumentException("Message id cannot be empty.", nameof(messageId));

        Address = address;
        MessageId = messageId;
    }

    public ApplicationAddress Address { get; }

    public MessageId MessageId { get; }

    public override string ToString() => $"{Address}/{MessageId}";
}

/// <summary>
/// Provider-neutral persisted representation of one captured application output.
/// </summary>
public sealed record DurableOutputEnvelope
{
    private static readonly IReadOnlyDictionary<string, string> EmptyHeaders =
        ImmutableDictionary.Create<string, string>(StringComparer.Ordinal);

    public const int CurrentSchemaVersion = 1;

    public DurableOutputEnvelope(
        ApplicationAddress address,
        string contractName,
        bool isError,
        JsonElement payload,
        FlowError? error,
        MessageId messageId,
        TraceId traceId,
        DateTimeOffset timestamp,
        DateTimeOffset capturedAt,
        CorrelationId? correlationId = null,
        MessageId? causationId = null,
        IReadOnlyDictionary<string, string>? headers = null,
        int schemaVersion = CurrentSchemaVersion)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.Kind != ApplicationAddressKind.WorkflowPort)
            throw new ArgumentException("Durable output requires a workflow port address.", nameof(address));
        ArgumentException.ThrowIfNullOrWhiteSpace(contractName);
        if (!string.Equals(contractName, contractName.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Contract name cannot have surrounding whitespace.", nameof(contractName));
        if (schemaVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        if (messageId.IsEmpty)
            throw new ArgumentException("Message id cannot be empty.", nameof(messageId));
        if (traceId.IsEmpty)
            throw new ArgumentException("Trace id cannot be empty.", nameof(traceId));
        if (isError && error is null)
            throw new ArgumentException("An error envelope requires an error.", nameof(error));
        if (!isError && error is not null)
            throw new ArgumentException("A value envelope cannot contain an error.", nameof(error));
        if (isError && payload.ValueKind != JsonValueKind.Null)
            throw new ArgumentException("An error envelope must contain a null value payload.", nameof(payload));
        if (payload.ValueKind == JsonValueKind.Undefined)
            throw new ArgumentException("Payload must contain a JSON value.", nameof(payload));

        Address = address;
        ContractName = contractName;
        IsError = isError;
        Payload = payload.Clone();
        Error = error;
        MessageId = messageId;
        TraceId = traceId;
        Timestamp = timestamp;
        CapturedAt = capturedAt;
        CorrelationId = correlationId is { IsEmpty: false } ? correlationId : null;
        CausationId = causationId is { IsEmpty: false } ? causationId : null;
        Headers = CopyHeaders(headers);
        SchemaVersion = schemaVersion;
    }

    public ApplicationAddress Address { get; }

    public string ContractName { get; }

    public bool IsError { get; }

    public JsonElement Payload { get; }

    public FlowError? Error { get; }

    public MessageId MessageId { get; }

    public TraceId TraceId { get; }

    public DateTimeOffset Timestamp { get; }

    public DateTimeOffset CapturedAt { get; }

    public CorrelationId? CorrelationId { get; }

    public MessageId? CausationId { get; }

    public IReadOnlyDictionary<string, string> Headers { get; }

    public int SchemaVersion { get; }

    public DurableOutputKey Key => new(Address, MessageId);

    /// <summary>
    /// Compares persisted content for idempotent-enqueue conflict detection.
    /// </summary>
    /// <remarks>
    /// <see cref="CapturedAt"/> is provider metadata and is deliberately excluded.
    /// </remarks>
    public bool HasSameContent(DurableOutputEnvelope other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Address == other.Address &&
               string.Equals(ContractName, other.ContractName, StringComparison.Ordinal) &&
               IsError == other.IsError &&
               JsonEquivalent(Payload, other.Payload) &&
               ErrorEquivalent(Error, other.Error) &&
               MessageId == other.MessageId &&
               TraceId == other.TraceId &&
               Timestamp.UtcTicks == other.Timestamp.UtcTicks &&
               Timestamp.Offset == other.Timestamp.Offset &&
               CorrelationId == other.CorrelationId &&
               CausationId == other.CausationId &&
               SchemaVersion == other.SchemaVersion &&
               Headers.Count == other.Headers.Count &&
               Headers.All(header =>
                   other.Headers.TryGetValue(header.Key, out var value) &&
                   string.Equals(header.Value, value, StringComparison.Ordinal));
    }

    private static bool ErrorEquivalent(FlowError? left, FlowError? right)
    {
        if (left is null || right is null)
            return left is null && right is null;

        return string.Equals(left.Code, right.Code, StringComparison.Ordinal) &&
               string.Equals(left.Message, right.Message, StringComparison.Ordinal) &&
               string.Equals(left.Category, right.Category, StringComparison.Ordinal) &&
               left.IsTransient == right.IsTransient &&
               (left.Details is null || right.Details is null
                   ? left.Details is null && right.Details is null
                   : JsonEquivalent(left.Details.Value, right.Details.Value));
    }

    private static bool JsonEquivalent(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
            return false;

        switch (left.ValueKind)
        {
            case JsonValueKind.Object:
                {
                    var leftProperties = left.EnumerateObject().ToArray();
                    var rightProperties = right.EnumerateObject().ToArray();
                    if (leftProperties.Length != rightProperties.Length)
                        return false;

                    foreach (var property in leftProperties)
                    {
                        var matches = rightProperties
                            .Where(candidate => string.Equals(
                                candidate.Name,
                                property.Name,
                                StringComparison.Ordinal))
                            .ToArray();
                        if (matches.Length != 1 || !JsonEquivalent(property.Value, matches[0].Value))
                            return false;
                    }

                    return true;
                }
            case JsonValueKind.Array:
                return left.EnumerateArray().SequenceEqual(
                    right.EnumerateArray(),
                    JsonElementEqualityComparer.Instance);
            case JsonValueKind.String:
                return string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal);
            case JsonValueKind.Number:
                return string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal);
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return true;
            default:
                return false;
        }
    }

    private sealed class JsonElementEqualityComparer : IEqualityComparer<JsonElement>
    {
        public static JsonElementEqualityComparer Instance { get; } = new();

        public bool Equals(JsonElement left, JsonElement right) => JsonEquivalent(left, right);

        public int GetHashCode(JsonElement value)
            => StringComparer.Ordinal.GetHashCode(value.GetRawText());
    }

    private static IReadOnlyDictionary<string, string> CopyHeaders(
        IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null || headers.Count == 0)
            return EmptyHeaders;

        var copy = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var header in headers)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(header.Key);
            copy.Add(
                header.Key,
                header.Value ?? throw new ArgumentException(
                    "Headers cannot contain null values.",
                    nameof(headers)));
        }

        return copy.ToImmutable();
    }
}

public enum DurableOutputEnqueueStatus
{
    Enqueued = 1,
    AlreadyExists = 2,
    Conflict = 3
}

public sealed record DurableOutputEnqueueResult
{
    public DurableOutputEnqueueResult(
        DurableOutputKey key,
        DurableOutputEnqueueStatus status)
    {
        if (key.Address is null || key.MessageId.IsEmpty)
            throw new ArgumentException("Durable output key must contain an address and message id.", nameof(key));
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));

        Key = key;
        Status = status;
    }

    public DurableOutputKey Key { get; }

    public DurableOutputEnqueueStatus Status { get; }

    public bool IsAccepted => Status is
        DurableOutputEnqueueStatus.Enqueued or DurableOutputEnqueueStatus.AlreadyExists;
}

/// <summary>
/// Atomic persistence boundary implemented by durable-output providers.
/// </summary>
/// <remarks>
/// Enqueue must be idempotent by <see cref="DurableOutputKey"/>. A repeated key with
/// equivalent message content returns <see cref="DurableOutputEnqueueStatus.AlreadyExists"/>;
/// a repeated key with different content returns <see cref="DurableOutputEnqueueStatus.Conflict"/>.
/// Capture time is provider metadata and does not make otherwise equivalent message content conflict.
/// Cancellation before the atomic commit leaves ownership with the caller. Cancellation after commit
/// must not retract the accepted record.
/// </remarks>
public interface IDurableOutputStore
{
    ValueTask<DurableOutputEnqueueResult> EnqueueAsync(
        DurableOutputEnvelope envelope,
        CancellationToken cancellationToken = default);
}
