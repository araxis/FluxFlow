using System.Collections.Immutable;
using FluxFlow.Composition.Addressing;

namespace FluxFlow.Engine.DurableInput;

/// <summary>
/// Bounded provider-neutral query for current durable-input dead letters.
/// </summary>
public sealed record DurableInputDeadLetterQuery
{
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 200;

    public DurableInputDeadLetterQuery(
        ApplicationAddress? address = null,
        DurableInputFailureKind? failureKind = null,
        DateTimeOffset? deadLetteredFrom = null,
        DateTimeOffset? deadLetteredBefore = null,
        DurableInputDeadLetterCursor? cursor = null,
        int pageSize = DefaultPageSize)
    {
        if (failureKind is { } kind && !Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(failureKind));
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
        FailureKind = failureKind;
        DeadLetteredFrom = deadLetteredFrom;
        DeadLetteredBefore = deadLetteredBefore;
        Cursor = cursor;
        PageSize = pageSize;
    }

    public ApplicationAddress? Address { get; }

    public DurableInputFailureKind? FailureKind { get; }

    public DateTimeOffset? DeadLetteredFrom { get; }

    public DateTimeOffset? DeadLetteredBefore { get; }

    public DurableInputDeadLetterCursor? Cursor { get; }

    public int PageSize { get; }
}

/// <summary>
/// Stable keyset position in the public dead-letter ordering.
/// </summary>
public sealed record DurableInputDeadLetterCursor
{
    public DurableInputDeadLetterCursor(
        DateTimeOffset deadLetteredAt,
        DurableInputKey key)
    {
        DurableInputValidation.ValidateKey(key);
        DeadLetteredAt = deadLetteredAt;
        Key = key;
    }

    public DateTimeOffset DeadLetteredAt { get; }

    public DurableInputKey Key { get; }
}

/// <summary>
/// Payload-free operational metadata for a current dead letter.
/// </summary>
public sealed record DurableInputDeadLetterSummary
{
    public DurableInputDeadLetterSummary(
        DurableInputKey key,
        string contractName,
        int envelopeSchemaVersion,
        bool isError,
        DateTimeOffset enqueuedAt,
        int attempt,
        DurableInputFailureKind failureKind,
        DateTimeOffset deadLetteredAt,
        long generation)
    {
        DurableInputValidation.ValidateKey(key);
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
        if (!Enum.IsDefined(failureKind))
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        if (generation <= 0)
            throw new ArgumentOutOfRangeException(nameof(generation));

        Key = key;
        ContractName = contractName;
        EnvelopeSchemaVersion = envelopeSchemaVersion;
        IsError = isError;
        EnqueuedAt = enqueuedAt;
        Attempt = attempt;
        FailureKind = failureKind;
        DeadLetteredAt = deadLetteredAt;
        Generation = generation;
    }

    public DurableInputKey Key { get; }

    public string ContractName { get; }

    public int EnvelopeSchemaVersion { get; }

    public bool IsError { get; }

    public DateTimeOffset EnqueuedAt { get; }

    public int Attempt { get; }

    public DurableInputFailureKind FailureKind { get; }

    public DateTimeOffset DeadLetteredAt { get; }

    public long Generation { get; }
}

/// <summary>
/// One bounded page of current dead-letter summaries.
/// </summary>
public sealed record DurableInputDeadLetterPage
{
    private readonly ImmutableArray<DurableInputDeadLetterSummary> _items;

    public DurableInputDeadLetterPage(
        IEnumerable<DurableInputDeadLetterSummary> items,
        DurableInputDeadLetterCursor? nextCursor)
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
            if (last.DeadLetteredAt != nextCursor.DeadLetteredAt ||
                last.Key != nextCursor.Key)
            {
                throw new ArgumentException(
                    "The continuation cursor must identify the last returned item.",
                    nameof(nextCursor));
            }
        }

        NextCursor = nextCursor;
    }

    public IReadOnlyList<DurableInputDeadLetterSummary> Items => _items;

    public DurableInputDeadLetterCursor? NextCursor { get; }

    public bool HasMore => NextCursor is not null;
}

/// <summary>
/// Complete envelope and current operational metadata for one dead letter.
/// </summary>
public sealed record DurableInputDeadLetterDetails
{
    public DurableInputDeadLetterDetails(
        DurableInputEnvelope envelope,
        int attempt,
        DurableInputFailure failure,
        DateTimeOffset deadLetteredAt,
        long generation)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (attempt <= 0)
            throw new ArgumentOutOfRangeException(nameof(attempt));
        ArgumentNullException.ThrowIfNull(failure);
        if (generation <= 0)
            throw new ArgumentOutOfRangeException(nameof(generation));

        Envelope = envelope;
        Attempt = attempt;
        Failure = failure;
        DeadLetteredAt = deadLetteredAt;
        Generation = generation;
    }

    public DurableInputEnvelope Envelope { get; }

    public int Attempt { get; }

    public DurableInputFailure Failure { get; }

    public DateTimeOffset DeadLetteredAt { get; }

    public long Generation { get; }
}

/// <summary>
/// Generation-protected request to return one current dead letter to Pending.
/// </summary>
public sealed record DurableInputReplay
{
    public DurableInputReplay(
        DurableInputKey key,
        long expectedGeneration,
        DateTimeOffset replayedAt,
        DateTimeOffset nextAttemptAt)
    {
        DurableInputValidation.ValidateKey(key);
        if (expectedGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedGeneration));
        if (nextAttemptAt < replayedAt)
            throw new ArgumentOutOfRangeException(nameof(nextAttemptAt));

        Key = key;
        ExpectedGeneration = expectedGeneration;
        ReplayedAt = replayedAt;
        NextAttemptAt = nextAttemptAt;
    }

    public DurableInputKey Key { get; }

    public long ExpectedGeneration { get; }

    public DateTimeOffset ReplayedAt { get; }

    public DateTimeOffset NextAttemptAt { get; }
}

public enum DurableInputReplayStatus
{
    Replayed = 1,
    NotFound = 2,
    NotDeadLettered = 3,
    GenerationMismatch = 4
}

public sealed record DurableInputReplayResult
{
    public DurableInputReplayResult(DurableInputKey key, DurableInputReplayStatus status)
    {
        DurableInputValidation.ValidateKey(key);
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        Key = key;
        Status = status;
    }

    public DurableInputKey Key { get; }

    public DurableInputReplayStatus Status { get; }

    public bool IsReplayed => Status == DurableInputReplayStatus.Replayed;
}

/// <summary>
/// Optional operational capability implemented by durable-input providers that support
/// current dead-letter inspection and explicit replay.
/// </summary>
public interface IDurableInputDeadLetterStore
{
    ValueTask<DurableInputDeadLetterPage> ListAsync(
        DurableInputDeadLetterQuery query,
        CancellationToken cancellationToken = default);

    ValueTask<DurableInputDeadLetterDetails?> GetAsync(
        DurableInputKey key,
        CancellationToken cancellationToken = default);

    ValueTask<DurableInputReplayResult> ReplayAsync(
        DurableInputReplay replay,
        CancellationToken cancellationToken = default);
}
