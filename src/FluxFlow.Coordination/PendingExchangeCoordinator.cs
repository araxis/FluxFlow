namespace FluxFlow.Coordination;

/// <summary>
/// Coordinates bounded keyed exchanges without transport or component semantics.
/// </summary>
public sealed class PendingExchangeCoordinator<TKey, TContext, TOutcome> : IAsyncDisposable
    where TKey : notnull
{
    private static readonly TimeSpan MaxTimerDueTime = TimeSpan.FromMilliseconds(uint.MaxValue - 1d);

    private readonly object _gate = new();
    private readonly Dictionary<TKey, PendingEntry> _pending = [];
    private readonly PriorityQueue<DeadlineEntry, (long DeadlineTicks, long Sequence)> _deadlines = new();
    private readonly Dictionary<TKey, PendingExchangeCompletionKind> _settled = [];
    private readonly Queue<TKey> _settledOrder = [];
    private readonly PendingExchangeCoordinatorOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ITimer _timer;
    private long _sequence;
    private bool _stopped;
    private int _timerDisposed;

    public PendingExchangeCoordinator(
        PendingExchangeCoordinatorOptions? options = null,
        TimeProvider? timeProvider = null,
        IEqualityComparer<TKey>? keyComparer = null)
    {
        _options = options ?? new PendingExchangeCoordinatorOptions();
        ValidateOptions(_options);
        _timeProvider = timeProvider ?? TimeProvider.System;

        if (keyComparer is not null)
        {
            _pending = new Dictionary<TKey, PendingEntry>(keyComparer);
            _settled = new Dictionary<TKey, PendingExchangeCompletionKind>(keyComparer);
        }

        _timer = _timeProvider.CreateTimer(
            static state => ((PendingExchangeCoordinator<TKey, TContext, TOutcome>)state!).OnTimer(),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
    }

    public int PendingCount
    {
        get
        {
            lock (_gate)
                return _pending.Count;
        }
    }

    public bool IsStopped
    {
        get
        {
            lock (_gate)
                return _stopped;
        }
    }

    public PendingExchangeStart<TKey, TContext, TOutcome> TryStart(
        TKey key,
        TContext context,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(context);

        var effectiveTimeout = timeout ?? _options.DefaultTimeout;
        if (effectiveTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), effectiveTimeout, "Timeout must be greater than zero.");

        lock (_gate)
        {
            if (_stopped)
                return Start(PendingExchangeStartStatus.Stopped);
            if (_pending.ContainsKey(key) || _settled.ContainsKey(key))
                return Start(PendingExchangeStartStatus.Duplicate);
            if (_pending.Count >= _options.MaxPending)
                return Start(PendingExchangeStartStatus.CapacityReached);

            var now = _timeProvider.GetUtcNow();
            if (effectiveTimeout > DateTimeOffset.MaxValue - now)
                throw new ArgumentOutOfRangeException(nameof(timeout), effectiveTimeout, "Timeout exceeds the supported deadline range.");

            var sequence = ++_sequence;
            var deadline = now + effectiveTimeout;
            var completion = new TaskCompletionSource<PendingExchangeCompletion<TKey, TContext, TOutcome>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _pending.Add(key, new PendingEntry(context, deadline, sequence, completion));
            _deadlines.Enqueue(
                new DeadlineEntry(key, sequence, deadline),
                (deadline.UtcTicks, sequence));
            ScheduleTimerUnsafe(now);

            return new PendingExchangeStart<TKey, TContext, TOutcome>(
                PendingExchangeStartStatus.Accepted,
                completion.Task);
        }
    }

    public PendingExchangeFeedback<TKey, TContext, TOutcome> TryResolve(TKey key, TOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return TrySettle(key, PendingExchangeCompletionKind.Resolved, outcome, error: null);
    }

    public PendingExchangeFeedback<TKey, TContext, TOutcome> TryFault(TKey key, Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return TrySettle(key, PendingExchangeCompletionKind.Faulted, outcome: default, error);
    }

    public PendingExchangeFeedback<TKey, TContext, TOutcome> TryCancel(
        TKey key,
        OperationCanceledException? error = null)
        => TrySettle(
            key,
            PendingExchangeCompletionKind.Cancelled,
            outcome: default,
            error ?? new OperationCanceledException("The pending exchange was cancelled."));

    public IReadOnlyList<PendingExchangeCompletion<TKey, TContext, TOutcome>> Stop(Exception? error = null)
        => SettleAll(
            error is null ? PendingExchangeCompletionKind.Stopped : PendingExchangeCompletionKind.Faulted,
            error,
            stop: true);

    public IReadOnlyList<PendingExchangeCompletion<TKey, TContext, TOutcome>> FaultAll(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return SettleAll(PendingExchangeCompletionKind.Faulted, error, stop: false);
    }

    private IReadOnlyList<PendingExchangeCompletion<TKey, TContext, TOutcome>> SettleAll(
        PendingExchangeCompletionKind kind,
        Exception? error,
        bool stop)
    {
        List<(PendingEntry Entry, PendingExchangeCompletion<TKey, TContext, TOutcome> Completion)> settled;
        lock (_gate)
        {
            if (_stopped)
                return [];

            _stopped = stop;
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            var now = _timeProvider.GetUtcNow();
            settled = _pending
                .OrderBy(static pair => pair.Value.Sequence)
                .Select(pair =>
                {
                    var completion = CreateCompletion(pair.Key, pair.Value, kind, outcome: default, error, now);
                    RememberSettlementUnsafe(pair.Key, kind);
                    return (pair.Value, completion);
                })
                .ToList();
            _pending.Clear();
            _deadlines.Clear();
        }

        foreach (var item in settled)
            item.Entry.Completion.TrySetResult(item.Completion);

        return settled.Select(static item => item.Completion).ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        if (Interlocked.Exchange(ref _timerDisposed, 1) == 0)
            await _timer.DisposeAsync().ConfigureAwait(false);
    }

    private PendingExchangeFeedback<TKey, TContext, TOutcome> TrySettle(
        TKey key,
        PendingExchangeCompletionKind kind,
        TOutcome? outcome,
        Exception? error)
    {
        ArgumentNullException.ThrowIfNull(key);

        PendingEntry? entry;
        PendingExchangeCompletion<TKey, TContext, TOutcome>? completion;
        lock (_gate)
        {
            if (_stopped)
                return Feedback(PendingExchangeFeedbackStatus.Stopped);
            if (!_pending.Remove(key, out entry))
                return Feedback(ClassifyMissingUnsafe(key));

            completion = CreateCompletion(
                key,
                entry,
                kind,
                outcome,
                error,
                _timeProvider.GetUtcNow());
            RememberSettlementUnsafe(key, kind);
            CompactDeadlinesUnsafe();
            ScheduleTimerUnsafe(_timeProvider.GetUtcNow());
        }

        entry.Completion.TrySetResult(completion);
        return new PendingExchangeFeedback<TKey, TContext, TOutcome>(
            PendingExchangeFeedbackStatus.Resolved,
            completion);
    }

    private void OnTimer()
    {
        List<(PendingEntry Entry, PendingExchangeCompletion<TKey, TContext, TOutcome> Completion)> expired = [];
        lock (_gate)
        {
            if (_stopped)
                return;

            var now = _timeProvider.GetUtcNow();
            while (_deadlines.TryPeek(out var deadline, out var priority) && priority.DeadlineTicks <= now.UtcTicks)
            {
                _deadlines.Dequeue();
                if (!_pending.TryGetValue(deadline.Key, out var entry) || entry.Sequence != deadline.Sequence)
                    continue;

                _pending.Remove(deadline.Key);
                var completion = CreateCompletion(
                    deadline.Key,
                    entry,
                    PendingExchangeCompletionKind.TimedOut,
                    outcome: default,
                    error: null,
                    now);
                RememberSettlementUnsafe(deadline.Key, PendingExchangeCompletionKind.TimedOut);
                expired.Add((entry, completion));
            }

            ScheduleTimerUnsafe(now);
        }

        foreach (var item in expired)
            item.Entry.Completion.TrySetResult(item.Completion);
    }

    private void ScheduleTimerUnsafe(DateTimeOffset now)
    {
        while (_deadlines.TryPeek(out var deadline, out _))
        {
            if (_pending.TryGetValue(deadline.Key, out var entry) && entry.Sequence == deadline.Sequence)
                break;
            _deadlines.Dequeue();
        }

        if (!_deadlines.TryPeek(out var next, out _))
        {
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }

        var dueTime = next.Deadline <= now ? TimeSpan.Zero : next.Deadline - now;
        if (dueTime > MaxTimerDueTime)
            dueTime = MaxTimerDueTime;
        _timer.Change(dueTime, Timeout.InfiniteTimeSpan);
    }

    private void CompactDeadlinesUnsafe()
    {
        if (_deadlines.Count <= (_pending.Count * 2) + 64)
            return;

        _deadlines.Clear();
        foreach (var pair in _pending)
        {
            _deadlines.Enqueue(
                new DeadlineEntry(pair.Key, pair.Value.Sequence, pair.Value.Deadline),
                (pair.Value.Deadline.UtcTicks, pair.Value.Sequence));
        }
    }

    private void RememberSettlementUnsafe(TKey key, PendingExchangeCompletionKind kind)
    {
        _settled.Add(key, kind);
        _settledOrder.Enqueue(key);
        while (_settledOrder.Count > _options.SettledKeyCapacity)
            _settled.Remove(_settledOrder.Dequeue());
    }

    private PendingExchangeFeedbackStatus ClassifyMissingUnsafe(TKey key)
    {
        if (!_settled.TryGetValue(key, out var kind))
            return PendingExchangeFeedbackStatus.NotFound;

        return kind == PendingExchangeCompletionKind.Resolved
            ? PendingExchangeFeedbackStatus.Duplicate
            : PendingExchangeFeedbackStatus.Late;
    }

    private static PendingExchangeCompletion<TKey, TContext, TOutcome> CreateCompletion(
        TKey key,
        PendingEntry entry,
        PendingExchangeCompletionKind kind,
        TOutcome? outcome,
        Exception? error,
        DateTimeOffset completedAt)
        => new(key, entry.Context, kind, outcome, error, completedAt);

    private static PendingExchangeStart<TKey, TContext, TOutcome> Start(PendingExchangeStartStatus status)
        => new(status, completion: null);

    private static PendingExchangeFeedback<TKey, TContext, TOutcome> Feedback(PendingExchangeFeedbackStatus status)
        => new(status, completion: null);

    private static void ValidateOptions(PendingExchangeCoordinatorOptions options)
    {
        if (options.DefaultTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), options.DefaultTimeout, "Default timeout must be greater than zero.");
        if (options.MaxPending <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), options.MaxPending, "Maximum pending count must be greater than zero.");
        if (options.SettledKeyCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), options.SettledKeyCapacity, "Settled key capacity must be greater than zero.");
    }

    private sealed record PendingEntry(
        TContext Context,
        DateTimeOffset Deadline,
        long Sequence,
        TaskCompletionSource<PendingExchangeCompletion<TKey, TContext, TOutcome>> Completion);

    private readonly record struct DeadlineEntry(TKey Key, long Sequence, DateTimeOffset Deadline);
}
