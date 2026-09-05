namespace FluxFlow.Engine.DurableInput;

/// <summary>
/// Selects the explicit time boundary for a durable-input status snapshot.
/// </summary>
public sealed record DurableInputStatusQuery
{
    public DurableInputStatusQuery(DateTimeOffset observedAt)
    {
        ObservedAt = observedAt;
    }

    public DateTimeOffset ObservedAt { get; }
}

/// <summary>
/// Payload-free operational state for one durable-input store.
/// </summary>
public sealed record DurableInputStatusSnapshot
{
    public DurableInputStatusSnapshot(
        DateTimeOffset observedAt,
        long pendingCount,
        long readyPendingCount,
        long leasedCount,
        long expiredLeaseCount,
        long deliveredCount,
        long deadLetteredCount,
        DateTimeOffset? oldestReadyAt,
        DateTimeOffset? nextLeaseExpiry)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pendingCount);
        ArgumentOutOfRangeException.ThrowIfNegative(readyPendingCount);
        ArgumentOutOfRangeException.ThrowIfNegative(leasedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(expiredLeaseCount);
        ArgumentOutOfRangeException.ThrowIfNegative(deliveredCount);
        ArgumentOutOfRangeException.ThrowIfNegative(deadLetteredCount);

        if (readyPendingCount > pendingCount)
            throw new ArgumentException("Ready pending count cannot exceed pending count.", nameof(readyPendingCount));
        if (expiredLeaseCount > leasedCount)
            throw new ArgumentException("Expired lease count cannot exceed leased count.", nameof(expiredLeaseCount));

        var readyCount = checked(readyPendingCount + expiredLeaseCount);
        ValidateOldestReadyAt(observedAt, readyCount, oldestReadyAt);

        var activeLeaseCount = leasedCount - expiredLeaseCount;
        ValidateNextLeaseExpiry(observedAt, activeLeaseCount, nextLeaseExpiry);

        ObservedAt = observedAt;
        PendingCount = pendingCount;
        ReadyPendingCount = readyPendingCount;
        LeasedCount = leasedCount;
        ExpiredLeaseCount = expiredLeaseCount;
        DeliveredCount = deliveredCount;
        DeadLetteredCount = deadLetteredCount;
        OldestReadyAt = oldestReadyAt;
        NextLeaseExpiry = nextLeaseExpiry;
    }

    public DateTimeOffset ObservedAt { get; }

    public long PendingCount { get; }

    public long ReadyPendingCount { get; }

    public long LeasedCount { get; }

    public long ExpiredLeaseCount { get; }

    public long DeliveredCount { get; }

    public long DeadLetteredCount { get; }

    public DateTimeOffset? OldestReadyAt { get; }

    public DateTimeOffset? NextLeaseExpiry { get; }

    public long TotalCount => checked(
        checked(PendingCount + LeasedCount) +
        checked(DeliveredCount + DeadLetteredCount));

    private static void ValidateOldestReadyAt(
        DateTimeOffset observedAt,
        long readyCount,
        DateTimeOffset? oldestReadyAt)
    {
        if (readyCount == 0 && oldestReadyAt is not null)
            throw new ArgumentException("Oldest ready time requires at least one ready input.", nameof(oldestReadyAt));
        if (readyCount != 0 && oldestReadyAt is null)
            throw new ArgumentException("A ready input requires an oldest ready time.", nameof(oldestReadyAt));
        if (oldestReadyAt > observedAt)
            throw new ArgumentOutOfRangeException(nameof(oldestReadyAt), "Oldest ready time cannot be after the observation time.");
    }

    private static void ValidateNextLeaseExpiry(
        DateTimeOffset observedAt,
        long activeLeaseCount,
        DateTimeOffset? nextLeaseExpiry)
    {
        if (activeLeaseCount == 0 && nextLeaseExpiry is not null)
            throw new ArgumentException("Next lease expiry requires at least one active lease.", nameof(nextLeaseExpiry));
        if (activeLeaseCount != 0 && nextLeaseExpiry is null)
            throw new ArgumentException("An active lease requires a next lease expiry.", nameof(nextLeaseExpiry));
        if (nextLeaseExpiry <= observedAt)
            throw new ArgumentOutOfRangeException(nameof(nextLeaseExpiry), "Next lease expiry must be after the observation time.");
    }
}

/// <summary>
/// Optional read-only operational inspection for a durable-input provider.
/// </summary>
public interface IDurableInputStatusStore
{
    ValueTask<DurableInputStatusSnapshot> GetStatusAsync(
        DurableInputStatusQuery query,
        CancellationToken cancellationToken = default);
}
