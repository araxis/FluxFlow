namespace FluxFlow.Engine.DurableOutput;

/// <summary>
/// Selects the explicit time boundary for a durable-output status snapshot.
/// </summary>
public sealed record DurableOutputStatusQuery
{
    public DurableOutputStatusQuery(DateTimeOffset observedAt)
    {
        ObservedAt = observedAt;
    }

    public DateTimeOffset ObservedAt { get; }
}

/// <summary>
/// Payload-free operational state for one durable-output store.
/// </summary>
public sealed record DurableOutputStatusSnapshot
{
    public DurableOutputStatusSnapshot(
        DateTimeOffset observedAt,
        long capturedCount,
        long unmaterializedCount,
        long readyUnmaterializedCount,
        long pendingCount,
        long readyPendingCount,
        long leasedCount,
        long expiredLeaseCount,
        long completedCount,
        long deadLetteredCount,
        DateTimeOffset? oldestReadyAt,
        DateTimeOffset? nextLeaseExpiry)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capturedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(unmaterializedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(readyUnmaterializedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(pendingCount);
        ArgumentOutOfRangeException.ThrowIfNegative(readyPendingCount);
        ArgumentOutOfRangeException.ThrowIfNegative(leasedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(expiredLeaseCount);
        ArgumentOutOfRangeException.ThrowIfNegative(completedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(deadLetteredCount);

        if (readyUnmaterializedCount > unmaterializedCount)
            throw new ArgumentException("Ready unmaterialized count cannot exceed unmaterialized count.", nameof(readyUnmaterializedCount));
        if (readyPendingCount > pendingCount)
            throw new ArgumentException("Ready pending count cannot exceed pending count.", nameof(readyPendingCount));
        if (expiredLeaseCount > leasedCount)
            throw new ArgumentException("Expired lease count cannot exceed leased count.", nameof(expiredLeaseCount));

        var trackedDeliveryCount = checked(
            checked(pendingCount + leasedCount) +
            checked(completedCount + deadLetteredCount));
        if (checked(unmaterializedCount + trackedDeliveryCount) != capturedCount)
        {
            throw new ArgumentException(
                "Unmaterialized and tracked delivery counts must equal captured count.",
                nameof(capturedCount));
        }

        var readyCount = checked(
            checked(readyUnmaterializedCount + readyPendingCount) + expiredLeaseCount);
        ValidateOldestReadyAt(observedAt, readyCount, oldestReadyAt);

        var activeLeaseCount = leasedCount - expiredLeaseCount;
        ValidateNextLeaseExpiry(observedAt, activeLeaseCount, nextLeaseExpiry);

        ObservedAt = observedAt;
        CapturedCount = capturedCount;
        UnmaterializedCount = unmaterializedCount;
        ReadyUnmaterializedCount = readyUnmaterializedCount;
        PendingCount = pendingCount;
        ReadyPendingCount = readyPendingCount;
        LeasedCount = leasedCount;
        ExpiredLeaseCount = expiredLeaseCount;
        CompletedCount = completedCount;
        DeadLetteredCount = deadLetteredCount;
        OldestReadyAt = oldestReadyAt;
        NextLeaseExpiry = nextLeaseExpiry;
    }

    public DateTimeOffset ObservedAt { get; }

    public long CapturedCount { get; }

    public long UnmaterializedCount { get; }

    public long ReadyUnmaterializedCount { get; }

    public long PendingCount { get; }

    public long ReadyPendingCount { get; }

    public long LeasedCount { get; }

    public long ExpiredLeaseCount { get; }

    public long CompletedCount { get; }

    public long DeadLetteredCount { get; }

    public DateTimeOffset? OldestReadyAt { get; }

    public DateTimeOffset? NextLeaseExpiry { get; }

    public long TrackedDeliveryCount => checked(
        checked(PendingCount + LeasedCount) +
        checked(CompletedCount + DeadLetteredCount));

    public long ReadyCount => checked(
        checked(ReadyUnmaterializedCount + ReadyPendingCount) + ExpiredLeaseCount);

    private static void ValidateOldestReadyAt(
        DateTimeOffset observedAt,
        long readyCount,
        DateTimeOffset? oldestReadyAt)
    {
        if (readyCount == 0 && oldestReadyAt is not null)
            throw new ArgumentException("Oldest ready time requires at least one ready output.", nameof(oldestReadyAt));
        if (readyCount != 0 && oldestReadyAt is null)
            throw new ArgumentException("A ready output requires an oldest ready time.", nameof(oldestReadyAt));
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
/// Optional read-only operational inspection for a durable-output provider.
/// </summary>
public interface IDurableOutputStatusStore
{
    ValueTask<DurableOutputStatusSnapshot> GetStatusAsync(
        DurableOutputStatusQuery query,
        CancellationToken cancellationToken = default);
}
