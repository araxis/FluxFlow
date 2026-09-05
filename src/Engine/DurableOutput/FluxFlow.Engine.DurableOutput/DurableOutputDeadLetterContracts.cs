using System.Collections.Immutable;
using FluxFlow.Composition.Addressing;

namespace FluxFlow.Engine.DurableOutput;

/// <summary>
/// Stable reason recorded when a leased durable output cannot be delivered.
/// </summary>
public enum DurableOutputDeadLetterReason
{
    HandlerFailure = 1
}

/// <summary>
/// Bounded provider-neutral query for current durable-output dead letters.
/// </summary>
public sealed record DurableOutputDeadLetterQuery
{
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 200;

    public DurableOutputDeadLetterQuery(
        ApplicationAddress? address = null,
        DurableOutputDeadLetterReason? reason = null,
        DateTimeOffset? deadLetteredFrom = null,
        DateTimeOffset? deadLetteredBefore = null,
        DurableOutputDeadLetterCursor? cursor = null,
        int pageSize = DefaultPageSize)
    {
        if (reason is { } value && !Enum.IsDefined(value))
            throw new ArgumentOutOfRangeException(nameof(reason));
        if (deadLetteredFrom is { } from &&
            deadLetteredBefore is { } before &&
            from >= before)
        {
            throw new ArgumentException(
                "The inclusive dead-letter lower bound must precede the exclusive upper bound.",
                nameof(deadLetteredFrom));
        }

        if (pageSize is <= 0 or > MaximumPageSize)
            throw new ArgumentOutOfRangeException(nameof(pageSize));

        Address = address;
        Reason = reason;
        DeadLetteredFrom = deadLetteredFrom;
        DeadLetteredBefore = deadLetteredBefore;
        Cursor = cursor;
        PageSize = pageSize;
    }

    public ApplicationAddress? Address { get; }

    public DurableOutputDeadLetterReason? Reason { get; }

    public DateTimeOffset? DeadLetteredFrom { get; }

    public DateTimeOffset? DeadLetteredBefore { get; }

    public DurableOutputDeadLetterCursor? Cursor { get; }

    public int PageSize { get; }
}

/// <summary>
/// Stable keyset position in the public dead-letter ordering.
/// </summary>
public sealed record DurableOutputDeadLetterCursor
{
    public DurableOutputDeadLetterCursor(
        DateTimeOffset deadLetteredAt,
        DurableOutputKey key)
    {
        DurableOutputDeliveryValidation.ValidateKey(key);
        DeadLetteredAt = deadLetteredAt;
        Key = key;
    }

    public DateTimeOffset DeadLetteredAt { get; }

    public DurableOutputKey Key { get; }
}

/// <summary>
/// Payload-free operational metadata for a current durable-output dead letter.
/// </summary>
public sealed record DurableOutputDeadLetterSummary
{
    public DurableOutputDeadLetterSummary(
        DurableOutputKey key,
        string contractName,
        int envelopeSchemaVersion,
        bool isError,
        DateTimeOffset capturedAt,
        int attempt,
        DurableOutputDeadLetterReason reason,
        DateTimeOffset deadLetteredAt,
        long generation)
    {
        DurableOutputDeliveryValidation.ValidateKey(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractName);
        if (!string.Equals(contractName, contractName.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Contract name cannot have surrounding whitespace.",
                nameof(contractName));
        }

        if (envelopeSchemaVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(envelopeSchemaVersion));
        if (attempt <= 0)
            throw new ArgumentOutOfRangeException(nameof(attempt));
        if (!Enum.IsDefined(reason))
            throw new ArgumentOutOfRangeException(nameof(reason));
        if (generation <= 0)
            throw new ArgumentOutOfRangeException(nameof(generation));

        Key = key;
        ContractName = contractName;
        EnvelopeSchemaVersion = envelopeSchemaVersion;
        IsError = isError;
        CapturedAt = capturedAt;
        Attempt = attempt;
        Reason = reason;
        DeadLetteredAt = deadLetteredAt;
        Generation = generation;
    }

    public DurableOutputKey Key { get; }

    public string ContractName { get; }

    public int EnvelopeSchemaVersion { get; }

    public bool IsError { get; }

    public DateTimeOffset CapturedAt { get; }

    public int Attempt { get; }

    public DurableOutputDeadLetterReason Reason { get; }

    public DateTimeOffset DeadLetteredAt { get; }

    public long Generation { get; }
}

/// <summary>
/// One bounded page of current durable-output dead-letter summaries.
/// </summary>
public sealed record DurableOutputDeadLetterPage
{
    private readonly ImmutableArray<DurableOutputDeadLetterSummary> _items;

    public DurableOutputDeadLetterPage(
        IEnumerable<DurableOutputDeadLetterSummary> items,
        DurableOutputDeadLetterCursor? nextCursor)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = [.. items];
        if (_items.Any(static item => item is null))
            throw new ArgumentException("Dead-letter pages cannot contain null items.", nameof(items));

        if (nextCursor is not null)
        {
            if (_items.IsEmpty)
                throw new ArgumentException("An empty page cannot have a continuation cursor.", nameof(nextCursor));

            var last = _items[^1];
            if (!HasExactValue(last.DeadLetteredAt, nextCursor.DeadLetteredAt) ||
                last.Key != nextCursor.Key)
            {
                throw new ArgumentException(
                    "The continuation cursor must identify the last returned item.",
                    nameof(nextCursor));
            }
        }

        NextCursor = nextCursor;
    }

    public IReadOnlyList<DurableOutputDeadLetterSummary> Items => _items;

    public DurableOutputDeadLetterCursor? NextCursor { get; }

    public bool HasMore => NextCursor is not null;

    private static bool HasExactValue(DateTimeOffset left, DateTimeOffset right)
        => left.UtcTicks == right.UtcTicks && left.Offset == right.Offset;
}

/// <summary>
/// Complete envelope and current operational metadata for one dead letter.
/// </summary>
public sealed record DurableOutputDeadLetterDetails
{
    public DurableOutputDeadLetterDetails(
        DurableOutputEnvelope envelope,
        int attempt,
        DurableOutputDeadLetterReason reason,
        DateTimeOffset deadLetteredAt,
        long generation)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (attempt <= 0)
            throw new ArgumentOutOfRangeException(nameof(attempt));
        if (!Enum.IsDefined(reason))
            throw new ArgumentOutOfRangeException(nameof(reason));
        if (generation <= 0)
            throw new ArgumentOutOfRangeException(nameof(generation));

        Envelope = envelope;
        Attempt = attempt;
        Reason = reason;
        DeadLetteredAt = deadLetteredAt;
        Generation = generation;
    }

    public DurableOutputEnvelope Envelope { get; }

    public int Attempt { get; }

    public DurableOutputDeadLetterReason Reason { get; }

    public DateTimeOffset DeadLetteredAt { get; }

    public long Generation { get; }
}

/// <summary>
/// Generation-protected request to return one current dead letter to Pending.
/// </summary>
public sealed record DurableOutputReplay
{
    public DurableOutputReplay(
        DurableOutputKey key,
        long expectedGeneration,
        DateTimeOffset replayedAt,
        DateTimeOffset nextAttemptAt)
    {
        DurableOutputDeliveryValidation.ValidateKey(key);
        if (expectedGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedGeneration));
        if (nextAttemptAt < replayedAt)
            throw new ArgumentOutOfRangeException(nameof(nextAttemptAt));

        Key = key;
        ExpectedGeneration = expectedGeneration;
        ReplayedAt = replayedAt;
        NextAttemptAt = nextAttemptAt;
    }

    public DurableOutputKey Key { get; }

    public long ExpectedGeneration { get; }

    public DateTimeOffset ReplayedAt { get; }

    public DateTimeOffset NextAttemptAt { get; }
}

public enum DurableOutputReplayStatus
{
    Replayed = 1,
    NotFound = 2,
    NotDeadLettered = 3,
    GenerationMismatch = 4
}

public sealed record DurableOutputReplayResult
{
    public DurableOutputReplayResult(
        DurableOutputKey key,
        DurableOutputReplayStatus status)
    {
        DurableOutputDeliveryValidation.ValidateKey(key);
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));

        Key = key;
        Status = status;
    }

    public DurableOutputKey Key { get; }

    public DurableOutputReplayStatus Status { get; }

    public bool IsReplayed => Status == DurableOutputReplayStatus.Replayed;
}

/// <summary>
/// Optional operational capability for current durable-output dead-letter
/// inspection and explicit replay.
/// </summary>
public interface IDurableOutputDeadLetterStore
{
    ValueTask<DurableOutputDeadLetterPage> ListAsync(
        DurableOutputDeadLetterQuery query,
        CancellationToken cancellationToken = default);

    ValueTask<DurableOutputDeadLetterDetails?> GetAsync(
        DurableOutputKey key,
        CancellationToken cancellationToken = default);

    ValueTask<DurableOutputReplayResult> ReplayAsync(
        DurableOutputReplay replay,
        CancellationToken cancellationToken = default);
}
