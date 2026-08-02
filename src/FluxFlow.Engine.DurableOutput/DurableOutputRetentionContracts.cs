using FluxFlow.Composition.Addressing;

namespace FluxFlow.Engine.DurableOutput;

/// <summary>
/// Selects one bounded batch of terminal durable-output records for permanent deletion.
/// </summary>
/// <remarks>
/// The cutoff is exclusive. Deleting completed records ends their idempotency window,
/// and deleting dead letters removes their replay source.
/// </remarks>
public sealed record DurableOutputRetentionRequest
{
    public const int DefaultMaxCount = 100;
    public const int MaximumMaxCount = 1_000;

    public DurableOutputRetentionRequest(
        DateTimeOffset terminalBefore,
        ApplicationAddress? address = null,
        int maxCount = DefaultMaxCount)
    {
        if (maxCount is <= 0 or > MaximumMaxCount)
            throw new ArgumentOutOfRangeException(nameof(maxCount));

        TerminalBefore = terminalBefore;
        Address = address;
        MaxCount = maxCount;
    }

    public DateTimeOffset TerminalBefore { get; }

    public ApplicationAddress? Address { get; }

    public int MaxCount { get; }
}

/// <summary>
/// Reports the number of terminal durable-output captures permanently deleted.
/// </summary>
public sealed record DurableOutputRetentionResult
{
    public DurableOutputRetentionResult(int deletedCount)
    {
        if (deletedCount < 0)
            throw new ArgumentOutOfRangeException(nameof(deletedCount));

        DeletedCount = deletedCount;
    }

    public int DeletedCount { get; }
}

/// <summary>
/// Optional operational capability for explicit, bounded deletion of terminal
/// durable-output captures and their delivery records.
/// </summary>
public interface IDurableOutputRetentionStore
{
    /// <summary>
    /// Permanently deletes completed capture parents whose delivery timestamp is
    /// earlier than the request cutoff.
    /// </summary>
    /// <remarks>Deletion ends the idempotency window for each removed identity.</remarks>
    ValueTask<DurableOutputRetentionResult> PurgeCompletedAsync(
        DurableOutputRetentionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes dead-lettered capture parents whose dead-letter timestamp
    /// is earlier than the request cutoff.
    /// </summary>
    /// <remarks>Deleted dead letters can no longer be inspected or replayed.</remarks>
    ValueTask<DurableOutputRetentionResult> PurgeDeadLettersAsync(
        DurableOutputRetentionRequest request,
        CancellationToken cancellationToken = default);
}
