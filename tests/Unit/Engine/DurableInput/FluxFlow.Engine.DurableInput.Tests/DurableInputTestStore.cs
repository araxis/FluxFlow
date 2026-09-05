using FluxFlow.Engine.DurableInput;

namespace FluxFlow.Engine.DurableInput.Tests;

/// <summary>
/// Executable provider-contract fixture used only by this test assembly.
/// It is not a product persistence provider.
/// </summary>
internal sealed class DurableInputTestStore : IDurableInputStore, IDurableInputLeaseRenewalStore
{
    private readonly object _gate = new();
    private readonly Dictionary<DurableInputKey, StoredInput> _inputs = [];
    private int _enqueueCalls;
    private int _leaseCalls;

    public Exception? EnqueueException { get; set; }

    public int EnqueueCalls => Volatile.Read(ref _enqueueCalls);

    public Exception? LeaseException { get; set; }

    public int LeaseCalls => Volatile.Read(ref _leaseCalls);

    public bool LoseNextDeliveredTransition { get; set; }

    public bool LoseNextRenewal { get; set; }

    public DurableInputTransitionStatus? ForcedRenewalStatus { get; set; }

    public DurableInputKey? RenewalResultKey { get; set; }

    public bool ReturnNullRenewalResult { get; set; }

    public Exception? RenewalException { get; set; }

    public List<DurableInputLeaseRequest> LeaseRequests { get; } = [];

    public TaskCompletionSource LeaseObserved { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource SecondLeaseObserved { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<DurableInputLeaseTransition> DeliveredTransitions { get; } = [];

    public List<DurableInputLeaseRenewal> Renewals { get; } = [];

    public TaskCompletionSource RenewalObserved { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<DurableInputRelease> Releases { get; } = [];

    public List<DurableInputDeadLetter> DeadLetters { get; } = [];

    public ValueTask<DurableInputEnqueueResult> EnqueueAsync(
        DurableInputEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(envelope);
        Interlocked.Increment(ref _enqueueCalls);
        if (EnqueueException is not null)
            throw EnqueueException;
        lock (_gate)
        {
            if (_inputs.TryGetValue(envelope.Key, out var existing))
            {
                var status = existing.Envelope.HasSameContent(envelope)
                    ? DurableInputEnqueueStatus.AlreadyExists
                    : DurableInputEnqueueStatus.Conflict;
                return ValueTask.FromResult(new DurableInputEnqueueResult(envelope.Key, status));
            }

            _inputs.Add(
                envelope.Key,
                new StoredInput(envelope)
                {
                    State = DurableInputState.Pending,
                    NextAttemptAt = envelope.EnqueuedAt
                });
            return ValueTask.FromResult(new DurableInputEnqueueResult(
                envelope.Key,
                DurableInputEnqueueStatus.Enqueued));
        }
    }

    public ValueTask<IReadOnlyList<DurableInputLease>> LeaseAsync(
        DurableInputLeaseRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var leaseCall = Interlocked.Increment(ref _leaseCalls);
        LeaseObserved.TrySetResult();
        if (leaseCall >= 2)
            SecondLeaseObserved.TrySetResult();
        if (LeaseException is not null)
            throw LeaseException;
        lock (_gate)
        {
            LeaseRequests.Add(request);
            var eligible = _inputs.Values
                .Where(input => input.IsEligible(request.Now))
                .OrderBy(input => input.EligibleAt)
                .ThenBy(input => input.Envelope.EnqueuedAt)
                .ThenBy(input => input.Envelope.Key.ToString(), StringComparer.Ordinal)
                .Take(request.MaxCount)
                .Select(input => input.Lease(request))
                .ToArray();
            return ValueTask.FromResult<IReadOnlyList<DurableInputLease>>(eligible);
        }
    }

    public ValueTask<DurableInputTransitionResult> MarkDeliveredAsync(
        DurableInputLeaseTransition transition,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            DeliveredTransitions.Add(transition);
            if (LoseNextDeliveredTransition)
            {
                LoseNextDeliveredTransition = false;
                return ValueTask.FromResult(new DurableInputTransitionResult(
                    transition.Key,
                    DurableInputTransitionStatus.LeaseLost));
            }

            return ValueTask.FromResult(Apply(
                transition.Key,
                transition.LeaseToken,
                transition.OccurredAt,
                static input => input.State = DurableInputState.Delivered));
        }
    }

    public ValueTask<DurableInputTransitionResult> RenewLeaseAsync(
        DurableInputLeaseRenewal renewal,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (RenewalException is not null)
            throw RenewalException;
        lock (_gate)
        {
            Renewals.Add(renewal);
            RenewalObserved.TrySetResult();
            if (ReturnNullRenewalResult)
                return ValueTask.FromResult<DurableInputTransitionResult>(null!);
            if (ForcedRenewalStatus is { } forcedStatus)
            {
                return ValueTask.FromResult(new DurableInputTransitionResult(
                    RenewalResultKey ?? renewal.Key,
                    forcedStatus));
            }
            if (LoseNextRenewal)
            {
                LoseNextRenewal = false;
                return ValueTask.FromResult(new DurableInputTransitionResult(
                    renewal.Key,
                    DurableInputTransitionStatus.LeaseLost));
            }

            if (!_inputs.TryGetValue(renewal.Key, out var input))
            {
                return ValueTask.FromResult(new DurableInputTransitionResult(
                    renewal.Key,
                    DurableInputTransitionStatus.NotFound));
            }
            if (input.State != DurableInputState.Leased)
            {
                return ValueTask.FromResult(new DurableInputTransitionResult(
                    renewal.Key,
                    DurableInputTransitionStatus.InvalidState));
            }
            if (input.LeaseToken != renewal.LeaseToken ||
                input.LeaseUntil <= renewal.RenewedAt)
            {
                return ValueTask.FromResult(new DurableInputTransitionResult(
                    renewal.Key,
                    DurableInputTransitionStatus.LeaseLost));
            }

            input.LeaseUntil = renewal.LeaseUntil;
            return ValueTask.FromResult(new DurableInputTransitionResult(
                renewal.Key,
                DurableInputTransitionStatus.Applied));
        }
    }

    public ValueTask<DurableInputTransitionResult> ReleaseAsync(
        DurableInputRelease release,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            Releases.Add(release);
            return ValueTask.FromResult(Apply(
                release.Key,
                release.LeaseToken,
                release.ReleasedAt,
                input =>
                {
                    input.State = DurableInputState.Pending;
                    input.NextAttemptAt = release.NextAttemptAt;
                    input.Failure = release.Failure;
                }));
        }
    }

    public ValueTask<DurableInputTransitionResult> DeadLetterAsync(
        DurableInputDeadLetter deadLetter,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            DeadLetters.Add(deadLetter);
            return ValueTask.FromResult(Apply(
                deadLetter.Key,
                deadLetter.LeaseToken,
                deadLetter.DeadLetteredAt,
                input =>
                {
                    input.State = DurableInputState.DeadLettered;
                    input.Failure = deadLetter.Failure;
                }));
        }
    }

    public StoredInputSnapshot Get(DurableInputKey key)
    {
        lock (_gate)
        {
            var input = _inputs[key];
            return new StoredInputSnapshot(
                input.Envelope,
                input.State,
                input.NextAttemptAt,
                input.LeaseToken,
                input.OwnerId,
                input.LeaseUntil,
                input.Attempt,
                input.Failure);
        }
    }

    private DurableInputTransitionResult Apply(
        DurableInputKey key,
        Guid leaseToken,
        DateTimeOffset occurredAt,
        Action<StoredInput> mutation)
    {
        if (!_inputs.TryGetValue(key, out var input))
            return new DurableInputTransitionResult(key, DurableInputTransitionStatus.NotFound);
        if (input.State != DurableInputState.Leased)
            return new DurableInputTransitionResult(key, DurableInputTransitionStatus.InvalidState);
        if (input.LeaseToken != leaseToken || input.LeaseUntil <= occurredAt)
            return new DurableInputTransitionResult(key, DurableInputTransitionStatus.LeaseLost);

        mutation(input);
        input.LeaseToken = null;
        input.OwnerId = null;
        input.LeaseUntil = null;
        return new DurableInputTransitionResult(key, DurableInputTransitionStatus.Applied);
    }

    internal sealed record StoredInputSnapshot(
        DurableInputEnvelope Envelope,
        DurableInputState State,
        DateTimeOffset NextAttemptAt,
        Guid? LeaseToken,
        string? OwnerId,
        DateTimeOffset? LeaseUntil,
        int Attempt,
        DurableInputFailure? Failure);

    private sealed class StoredInput(DurableInputEnvelope envelope)
    {
        public DurableInputEnvelope Envelope { get; } = envelope;

        public DurableInputState State { get; set; }

        public DateTimeOffset NextAttemptAt { get; set; }

        public Guid? LeaseToken { get; set; }

        public string? OwnerId { get; set; }

        public DateTimeOffset? LeaseUntil { get; set; }

        public int Attempt { get; set; }

        public DurableInputFailure? Failure { get; set; }

        public DateTimeOffset EligibleAt => State == DurableInputState.Leased
            ? LeaseUntil!.Value
            : NextAttemptAt;

        public bool IsEligible(DateTimeOffset now)
            => State == DurableInputState.Pending && NextAttemptAt <= now ||
               State == DurableInputState.Leased && LeaseUntil <= now;

        public DurableInputLease Lease(DurableInputLeaseRequest request)
        {
            State = DurableInputState.Leased;
            LeaseToken = Guid.NewGuid();
            OwnerId = request.OwnerId;
            LeaseUntil = request.LeaseUntil;
            Attempt++;
            return new DurableInputLease(
                Envelope,
                LeaseToken.Value,
                OwnerId,
                request.Now,
                LeaseUntil.Value,
                Attempt);
        }
    }
}
