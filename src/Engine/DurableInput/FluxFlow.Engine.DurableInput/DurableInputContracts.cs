using System.Collections.Immutable;
using System.Text.Json;
using FluxFlow.Composition.Addressing;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.DurableInput;

public readonly record struct DurableInputKey
{
    public DurableInputKey(ApplicationAddress address, MessageId messageId)
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

public enum DurableInputState
{
    Pending = 0,
    Leased = 1,
    Delivered = 2,
    DeadLettered = 3
}

/// <summary>
/// Provider-neutral persisted representation of one application input message.
/// </summary>
public sealed record DurableInputEnvelope
{
    private static readonly IReadOnlyDictionary<string, string> EmptyHeaders =
        ImmutableDictionary.Create<string, string>(StringComparer.Ordinal);

    public const int CurrentSchemaVersion = 1;

    public DurableInputEnvelope(
        ApplicationAddress address,
        string contractName,
        bool isError,
        JsonElement payload,
        FlowError? error,
        MessageId messageId,
        TraceId traceId,
        DateTimeOffset timestamp,
        DateTimeOffset enqueuedAt,
        CorrelationId? correlationId = null,
        MessageId? causationId = null,
        IReadOnlyDictionary<string, string>? headers = null,
        int schemaVersion = CurrentSchemaVersion)
    {
        ArgumentNullException.ThrowIfNull(address);
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
        EnqueuedAt = enqueuedAt;
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

    public DateTimeOffset EnqueuedAt { get; }

    public CorrelationId? CorrelationId { get; }

    public MessageId? CausationId { get; }

    public IReadOnlyDictionary<string, string> Headers { get; }

    public int SchemaVersion { get; }

    public DurableInputKey Key => new(Address, MessageId);

    /// <summary>
    /// Compares persisted content for idempotent-enqueue conflict detection.
    /// </summary>
    public bool HasSameContent(DurableInputEnvelope other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Address == other.Address &&
               string.Equals(ContractName, other.ContractName, StringComparison.Ordinal) &&
               IsError == other.IsError &&
               JsonEquivalent(Payload, other.Payload) &&
               ErrorEquivalent(Error, other.Error) &&
               MessageId == other.MessageId &&
               TraceId == other.TraceId &&
               Timestamp == other.Timestamp &&
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

        public int GetHashCode(JsonElement value) => StringComparer.Ordinal.GetHashCode(value.GetRawText());
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

public enum DurableInputEnqueueStatus
{
    Enqueued = 1,
    AlreadyExists = 2,
    Conflict = 3
}

public sealed record DurableInputEnqueueResult
{
    public DurableInputEnqueueResult(DurableInputKey key, DurableInputEnqueueStatus status)
    {
        DurableInputValidation.ValidateKey(key);
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        Key = key;
        Status = status;
    }

    public DurableInputKey Key { get; }

    public DurableInputEnqueueStatus Status { get; }

    public bool IsAccepted => Status is
        DurableInputEnqueueStatus.Enqueued or DurableInputEnqueueStatus.AlreadyExists;
}

public sealed record DurableInputLeaseRequest
{
    public DurableInputLeaseRequest(
        string ownerId,
        DateTimeOffset now,
        DateTimeOffset leaseUntil,
        int maxCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (!string.Equals(ownerId, ownerId.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Lease owner id cannot have surrounding whitespace.", nameof(ownerId));
        if (leaseUntil <= now)
            throw new ArgumentOutOfRangeException(nameof(leaseUntil));
        if (maxCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxCount));
        OwnerId = ownerId;
        Now = now;
        LeaseUntil = leaseUntil;
        MaxCount = maxCount;
    }

    public string OwnerId { get; }

    public DateTimeOffset Now { get; }

    public DateTimeOffset LeaseUntil { get; }

    public int MaxCount { get; }

}

public sealed record DurableInputLease
{
    public DurableInputLease(
        DurableInputEnvelope envelope,
        Guid leaseToken,
        string ownerId,
        DateTimeOffset leasedAt,
        DateTimeOffset leaseUntil,
        int attempt)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (leaseToken == Guid.Empty)
            throw new ArgumentException("Lease token cannot be empty.", nameof(leaseToken));
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (!string.Equals(ownerId, ownerId.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Lease owner id cannot have surrounding whitespace.", nameof(ownerId));
        if (leaseUntil <= leasedAt)
            throw new ArgumentOutOfRangeException(nameof(leaseUntil));
        if (attempt <= 0)
            throw new ArgumentOutOfRangeException(nameof(attempt));
        Envelope = envelope;
        LeaseToken = leaseToken;
        OwnerId = ownerId;
        LeasedAt = leasedAt;
        LeaseUntil = leaseUntil;
        Attempt = attempt;
    }

    public DurableInputEnvelope Envelope { get; }

    public Guid LeaseToken { get; }

    public string OwnerId { get; }

    public DateTimeOffset LeasedAt { get; }

    public DateTimeOffset LeaseUntil { get; }

    public int Attempt { get; }

}

public enum DurableInputFailureKind
{
    InputFull = 1,
    InputUnavailable = 2,
    InputCompleted = 3,
    InputAddressMissing = 4,
    UnknownContract = 5,
    UnsupportedSchemaVersion = 6,
    InvalidEnvelope = 7,
    DeserializationFailed = 8,
    NotMessageInput = 9,
    PayloadTypeMismatch = 10,
    MaximumAttemptsExceeded = 11,
    CompletionSourceUnavailable = 12,
    WorkflowCompletionFailed = 13,
    WorkflowCompletionTimedOut = 14
}

public sealed record DurableInputFailure
{
    public DurableInputFailure(DurableInputFailureKind kind, string description)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (!string.Equals(description, description.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Failure description cannot have surrounding whitespace.", nameof(description));
        Kind = kind;
        Description = description;
    }

    public DurableInputFailureKind Kind { get; }

    public string Description { get; }
}

public sealed record DurableInputLeaseTransition
{
    public DurableInputLeaseTransition(
        DurableInputKey key,
        Guid leaseToken,
        DateTimeOffset occurredAt)
    {
        DurableInputValidation.ValidateKey(key);
        DurableInputValidation.ValidateLeaseToken(leaseToken);
        Key = key;
        LeaseToken = leaseToken;
        OccurredAt = occurredAt;
    }

    public DurableInputKey Key { get; }

    public Guid LeaseToken { get; }

    public DateTimeOffset OccurredAt { get; }
}

public sealed record DurableInputLeaseRenewal
{
    public DurableInputLeaseRenewal(
        DurableInputKey key,
        Guid leaseToken,
        DateTimeOffset renewedAt,
        DateTimeOffset leaseUntil)
    {
        DurableInputValidation.ValidateKey(key);
        DurableInputValidation.ValidateLeaseToken(leaseToken);
        if (leaseUntil <= renewedAt)
            throw new ArgumentOutOfRangeException(nameof(leaseUntil));
        Key = key;
        LeaseToken = leaseToken;
        RenewedAt = renewedAt;
        LeaseUntil = leaseUntil;
    }

    public DurableInputKey Key { get; }

    public Guid LeaseToken { get; }

    public DateTimeOffset RenewedAt { get; }

    public DateTimeOffset LeaseUntil { get; }
}

public sealed record DurableInputRelease
{
    public DurableInputRelease(
        DurableInputKey key,
        Guid leaseToken,
        DateTimeOffset releasedAt,
        DateTimeOffset nextAttemptAt,
        DurableInputFailure failure)
    {
        DurableInputValidation.ValidateKey(key);
        DurableInputValidation.ValidateLeaseToken(leaseToken);
        ArgumentNullException.ThrowIfNull(failure);
        if (nextAttemptAt < releasedAt)
            throw new ArgumentOutOfRangeException(nameof(nextAttemptAt));
        Key = key;
        LeaseToken = leaseToken;
        ReleasedAt = releasedAt;
        NextAttemptAt = nextAttemptAt;
        Failure = failure;
    }

    public DurableInputKey Key { get; }

    public Guid LeaseToken { get; }

    public DateTimeOffset ReleasedAt { get; }

    public DateTimeOffset NextAttemptAt { get; }

    public DurableInputFailure Failure { get; }
}

public sealed record DurableInputDeadLetter
{
    public DurableInputDeadLetter(
        DurableInputKey key,
        Guid leaseToken,
        DateTimeOffset deadLetteredAt,
        DurableInputFailure failure)
    {
        DurableInputValidation.ValidateKey(key);
        DurableInputValidation.ValidateLeaseToken(leaseToken);
        ArgumentNullException.ThrowIfNull(failure);
        Key = key;
        LeaseToken = leaseToken;
        DeadLetteredAt = deadLetteredAt;
        Failure = failure;
    }

    public DurableInputKey Key { get; }

    public Guid LeaseToken { get; }

    public DateTimeOffset DeadLetteredAt { get; }

    public DurableInputFailure Failure { get; }
}

public enum DurableInputTransitionStatus
{
    Applied = 1,
    LeaseLost = 2,
    NotFound = 3,
    InvalidState = 4
}

public sealed record DurableInputTransitionResult
{
    public DurableInputTransitionResult(
        DurableInputKey key,
        DurableInputTransitionStatus status)
    {
        DurableInputValidation.ValidateKey(key);
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        Key = key;
        Status = status;
    }

    public DurableInputKey Key { get; }

    public DurableInputTransitionStatus Status { get; }

    public bool IsApplied => Status == DurableInputTransitionStatus.Applied;
}

/// <summary>
/// Atomic persistence boundary implemented by durable-input providers.
/// </summary>
/// <remarks>
/// Providers must make enqueue idempotent by <see cref="DurableInputKey"/>. Cancellation before
/// the atomic enqueue commit leaves ownership with the caller; after commit, cancellation must not
/// retract the entry. Eligible records are ordered by next-attempt time, original enqueue time,
/// and then stable key. Leasing atomically assigns one exclusive token and increments the attempt.
/// Every transition applies only when its lease token is still current and unexpired. Delivered
/// keys remain idempotency tombstones. Dead-lettered keys remain tombstones unless an
/// optional operational capability explicitly replays the current generation or a provider-owned
/// future retention policy removes them.
/// </remarks>
public interface IDurableInputStore
{
    ValueTask<DurableInputEnqueueResult> EnqueueAsync(
        DurableInputEnvelope envelope,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<DurableInputLease>> LeaseAsync(
        DurableInputLeaseRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<DurableInputTransitionResult> MarkDeliveredAsync(
        DurableInputLeaseTransition transition,
        CancellationToken cancellationToken = default);

    ValueTask<DurableInputTransitionResult> ReleaseAsync(
        DurableInputRelease release,
        CancellationToken cancellationToken = default);

    ValueTask<DurableInputTransitionResult> DeadLetterAsync(
        DurableInputDeadLetter deadLetter,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional atomic lease-renewal capability for durable-input providers.
/// </summary>
/// <remarks>
/// Implementations update only the lease expiry when the key is currently leased with the exact
/// token and remains unexpired at <see cref="DurableInputLeaseRenewal.RenewedAt"/>. A renewal must
/// never recreate or revive a missing, expired, or settled lease.
/// </remarks>
public interface IDurableInputLeaseRenewalStore
{
    ValueTask<DurableInputTransitionResult> RenewLeaseAsync(
        DurableInputLeaseRenewal renewal,
        CancellationToken cancellationToken = default);
}

internal static class DurableInputValidation
{
    public static void ValidateKey(DurableInputKey key)
    {
        if (key.Address is null || key.MessageId.IsEmpty)
            throw new ArgumentException("Durable input key must contain an address and message id.", nameof(key));
    }

    public static void ValidateLeaseToken(Guid leaseToken)
    {
        if (leaseToken == Guid.Empty)
            throw new ArgumentException("Lease token cannot be empty.", nameof(leaseToken));
    }
}
