namespace FluxFlow.Engine.DurableOutput;

/// <summary>
/// Requests exclusive ownership of at most one captured output for delivery.
/// </summary>
public sealed record DurableOutputDeliveryLeaseRequest
{
    public DurableOutputDeliveryLeaseRequest(
        string ownerId,
        DateTimeOffset now,
        DateTimeOffset leaseUntil)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (!string.Equals(ownerId, ownerId.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Lease owner id cannot have surrounding whitespace.", nameof(ownerId));
        if (leaseUntil <= now)
            throw new ArgumentOutOfRangeException(nameof(leaseUntil));

        OwnerId = ownerId;
        Now = now;
        LeaseUntil = leaseUntil;
    }

    public string OwnerId { get; }

    public DateTimeOffset Now { get; }

    public DateTimeOffset LeaseUntil { get; }
}

/// <summary>
/// Exclusive, time-bounded ownership of one captured output delivery attempt.
/// </summary>
public sealed record DurableOutputDeliveryLease
{
    public DurableOutputDeliveryLease(
        DurableOutputEnvelope envelope,
        Guid leaseToken,
        string ownerId,
        DateTimeOffset leasedAt,
        DateTimeOffset leaseUntil,
        int attempt)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        DurableOutputDeliveryValidation.ValidateLeaseToken(leaseToken);
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

    public DurableOutputEnvelope Envelope { get; }

    public Guid LeaseToken { get; }

    public string OwnerId { get; }

    public DateTimeOffset LeasedAt { get; }

    public DateTimeOffset LeaseUntil { get; }

    public int Attempt { get; }
}

/// <summary>
/// Extends the expiry of one currently owned delivery lease.
/// </summary>
public sealed record DurableOutputDeliveryLeaseRenewal
{
    public DurableOutputDeliveryLeaseRenewal(
        DurableOutputKey key,
        Guid leaseToken,
        DateTimeOffset renewedAt,
        DateTimeOffset leaseUntil)
    {
        DurableOutputDeliveryValidation.ValidateKey(key);
        DurableOutputDeliveryValidation.ValidateLeaseToken(leaseToken);
        if (leaseUntil <= renewedAt)
            throw new ArgumentOutOfRangeException(nameof(leaseUntil));

        Key = key;
        LeaseToken = leaseToken;
        RenewedAt = renewedAt;
        LeaseUntil = leaseUntil;
    }

    public DurableOutputKey Key { get; }

    public Guid LeaseToken { get; }

    public DateTimeOffset RenewedAt { get; }

    public DateTimeOffset LeaseUntil { get; }
}

/// <summary>
/// Completes one currently owned delivery lease.
/// </summary>
public sealed record DurableOutputDeliveryTransition
{
    public DurableOutputDeliveryTransition(
        DurableOutputKey key,
        Guid leaseToken,
        DateTimeOffset occurredAt)
    {
        DurableOutputDeliveryValidation.ValidateKey(key);
        DurableOutputDeliveryValidation.ValidateLeaseToken(leaseToken);
        Key = key;
        LeaseToken = leaseToken;
        OccurredAt = occurredAt;
    }

    public DurableOutputKey Key { get; }

    public Guid LeaseToken { get; }

    public DateTimeOffset OccurredAt { get; }
}

/// <summary>
/// Releases one currently owned delivery lease for a later attempt.
/// </summary>
public sealed record DurableOutputDeliveryRetry
{
    public DurableOutputDeliveryRetry(
        DurableOutputKey key,
        Guid leaseToken,
        DateTimeOffset releasedAt,
        DateTimeOffset nextAttemptAt)
    {
        DurableOutputDeliveryValidation.ValidateKey(key);
        DurableOutputDeliveryValidation.ValidateLeaseToken(leaseToken);
        if (nextAttemptAt < releasedAt)
            throw new ArgumentOutOfRangeException(nameof(nextAttemptAt));

        Key = key;
        LeaseToken = leaseToken;
        ReleasedAt = releasedAt;
        NextAttemptAt = nextAttemptAt;
    }

    public DurableOutputKey Key { get; }

    public Guid LeaseToken { get; }

    public DateTimeOffset ReleasedAt { get; }

    public DateTimeOffset NextAttemptAt { get; }
}

/// <summary>
/// Moves one currently owned delivery lease to the dead-letter state.
/// </summary>
public sealed record DurableOutputDeliveryDeadLetter
{
    public DurableOutputDeliveryDeadLetter(
        DurableOutputKey key,
        Guid leaseToken,
        DateTimeOffset deadLetteredAt,
        DurableOutputDeadLetterReason reason)
    {
        DurableOutputDeliveryValidation.ValidateKey(key);
        DurableOutputDeliveryValidation.ValidateLeaseToken(leaseToken);
        if (!Enum.IsDefined(reason))
            throw new ArgumentOutOfRangeException(nameof(reason));

        Key = key;
        LeaseToken = leaseToken;
        DeadLetteredAt = deadLetteredAt;
        Reason = reason;
    }

    public DurableOutputKey Key { get; }

    public Guid LeaseToken { get; }

    public DateTimeOffset DeadLetteredAt { get; }

    public DurableOutputDeadLetterReason Reason { get; }
}

public enum DurableOutputDeliveryTransitionStatus
{
    Applied = 1,
    LeaseLost = 2,
    NotFound = 3,
    InvalidState = 4
}

public sealed record DurableOutputDeliveryTransitionResult
{
    public DurableOutputDeliveryTransitionResult(
        DurableOutputKey key,
        DurableOutputDeliveryTransitionStatus status)
    {
        DurableOutputDeliveryValidation.ValidateKey(key);
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));

        Key = key;
        Status = status;
    }

    public DurableOutputKey Key { get; }

    public DurableOutputDeliveryTransitionStatus Status { get; }

    public bool IsApplied => Status == DurableOutputDeliveryTransitionStatus.Applied;
}

/// <summary>
/// Optional leased delivery capability implemented by durable-output providers.
/// </summary>
/// <remarks>
/// A lease is exclusive until its expiry. Renewal, completion, retry, and
/// dead-lettering are compare-and-set transitions that apply only to the current,
/// unexpired lease token. Delivered rows remain tombstones. A provider may implement
/// <see cref="IDurableOutputStore"/> without implementing this capability.
/// </remarks>
public interface IDurableOutputDeliveryStore
{
    ValueTask<DurableOutputDeliveryLease?> TryLeaseAsync(
        DurableOutputDeliveryLeaseRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<DurableOutputDeliveryTransitionResult> RenewLeaseAsync(
        DurableOutputDeliveryLeaseRenewal renewal,
        CancellationToken cancellationToken = default);

    ValueTask<DurableOutputDeliveryTransitionResult> CompleteAsync(
        DurableOutputDeliveryTransition transition,
        CancellationToken cancellationToken = default);

    ValueTask<DurableOutputDeliveryTransitionResult> RetryAsync(
        DurableOutputDeliveryRetry retry,
        CancellationToken cancellationToken = default);

    ValueTask<DurableOutputDeliveryTransitionResult> DeadLetterAsync(
        DurableOutputDeliveryDeadLetter deadLetter,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Host-owned destination boundary for one captured durable output.
/// </summary>
/// <remarks>
/// Delivery is at-least-once. Implementations should use
/// <see cref="DurableOutputEnvelope.Key"/> as an idempotency identity when the
/// destination supports idempotent operations.
/// </remarks>
public interface IDurableOutputDeliveryHandler
{
    ValueTask DeliverAsync(
        DurableOutputEnvelope envelope,
        CancellationToken cancellationToken);
}

internal static class DurableOutputDeliveryValidation
{
    public static void ValidateKey(DurableOutputKey key)
    {
        if (key.Address is null || key.MessageId.IsEmpty)
        {
            throw new ArgumentException(
                "Durable output delivery key must contain an address and message id.",
                nameof(key));
        }
    }

    public static void ValidateLeaseToken(Guid leaseToken)
    {
        if (leaseToken == Guid.Empty)
            throw new ArgumentException("Lease token cannot be empty.", nameof(leaseToken));
    }
}
