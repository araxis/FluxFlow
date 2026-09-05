using FluxFlow.Composition.Addressing;

namespace FluxFlow.Engine.DurableInput;

/// <summary>
/// Selects one bounded batch of terminal durable-input records for permanent deletion.
/// </summary>
/// <remarks>
/// The cutoff is exclusive. Deleting delivered records ends their deduplication window,
/// and deleting dead letters removes their replay source.
/// </remarks>
public sealed record DurableInputRetentionRequest
{
    public const int DefaultMaxCount = 100;
    public const int MaximumMaxCount = 1_000;

    public DurableInputRetentionRequest(
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
/// Reports the number of terminal durable-input records permanently deleted.
/// </summary>
public sealed record DurableInputRetentionResult
{
    public DurableInputRetentionResult(int deletedCount)
    {
        if (deletedCount < 0)
            throw new ArgumentOutOfRangeException(nameof(deletedCount));

        DeletedCount = deletedCount;
    }

    public int DeletedCount { get; }
}

/// <summary>
/// Optional operational capability for explicit, bounded deletion of terminal
/// durable-input records.
/// </summary>
public interface IDurableInputRetentionStore
{
    /// <summary>
    /// Permanently deletes delivered records whose delivery timestamp is earlier
    /// than the request cutoff.
    /// </summary>
    /// <remarks>Deletion ends the deduplication window for each removed identity.</remarks>
    ValueTask<DurableInputRetentionResult> PurgeDeliveredAsync(
        DurableInputRetentionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes dead letters whose dead-letter timestamp is earlier
    /// than the request cutoff.
    /// </summary>
    /// <remarks>Deleted dead letters can no longer be inspected or replayed.</remarks>
    ValueTask<DurableInputRetentionResult> PurgeDeadLettersAsync(
        DurableInputRetentionRequest request,
        CancellationToken cancellationToken = default);
}
