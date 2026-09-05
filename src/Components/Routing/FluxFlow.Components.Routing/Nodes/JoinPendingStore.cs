using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Routing.Nodes;

internal sealed class JoinPendingStore<TLeft, TRight>
{
    private readonly Dictionary<string, JoinPendingBucket<TLeft, TRight>> _pending;
    private readonly Queue<JoinDeadline> _deadlines = new();
    private readonly int _maxPending;

    internal JoinPendingStore(StringComparer comparer, int maxPending)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        if (maxPending <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPending));

        _pending = new Dictionary<string, JoinPendingBucket<TLeft, TRight>>(comparer);
        _maxPending = maxPending;
    }

    internal int Count { get; private set; }

    internal bool CanTrack => Count < _maxPending;

    internal bool TryTakeLeft(string key, out JoinPendingEntry<TLeft> entry)
    {
        if (!_pending.TryGetValue(key, out var bucket) || bucket.Lefts.Count == 0)
        {
            entry = default!;
            return false;
        }

        entry = bucket.Lefts.Dequeue();
        Count--;
        RemoveIfEmpty(key, bucket);
        return true;
    }

    internal bool TryTakeRight(string key, out JoinPendingEntry<TRight> entry)
    {
        if (!_pending.TryGetValue(key, out var bucket) || bucket.Rights.Count == 0)
        {
            entry = default!;
            return false;
        }

        entry = bucket.Rights.Dequeue();
        Count--;
        RemoveIfEmpty(key, bucket);
        return true;
    }

    internal void AddLeft(string key, FlowMessage<TLeft> message, DateTimeOffset receivedAt)
    {
        var bucket = GetOrCreateBucket(key);
        bucket.Lefts.Enqueue(new JoinPendingEntry<TLeft>(message, receivedAt));
        _deadlines.Enqueue(new JoinDeadline(key, FlowJoinSide.Left, receivedAt));
        Count++;
    }

    internal void AddRight(string key, FlowMessage<TRight> message, DateTimeOffset receivedAt)
    {
        var bucket = GetOrCreateBucket(key);
        bucket.Rights.Enqueue(new JoinPendingEntry<TRight>(message, receivedAt));
        _deadlines.Enqueue(new JoinDeadline(key, FlowJoinSide.Right, receivedAt));
        Count++;
    }

    internal IReadOnlyList<ExpiredJoinEntry<TLeft, TRight>> TakeExpired(
        DateTimeOffset now,
        TimeSpan timeout,
        bool force)
    {
        if (Count == 0)
        {
            _deadlines.Clear();
            return [];
        }

        var expired = new List<ExpiredJoinEntry<TLeft, TRight>>();
        while (_deadlines.Count > 0)
        {
            var deadline = _deadlines.Peek();
            if (!TryPeek(deadline, out var bucket))
            {
                _deadlines.Dequeue();
                continue;
            }

            if (!force && now - deadline.ReceivedAt < timeout)
                break;

            _deadlines.Dequeue();
            Count--;
            if (deadline.Side == FlowJoinSide.Left)
            {
                expired.Add(new ExpiredJoinEntry<TLeft, TRight>(
                    deadline.Key,
                    deadline.Side,
                    Left: bucket.Lefts.Dequeue()));
            }
            else
            {
                expired.Add(new ExpiredJoinEntry<TLeft, TRight>(
                    deadline.Key,
                    deadline.Side,
                    Right: bucket.Rights.Dequeue()));
            }

            RemoveIfEmpty(deadline.Key, bucket);
        }

        return expired;
    }

    internal DateTimeOffset? GetNextDueAt(TimeSpan timeout)
    {
        while (_deadlines.Count > 0)
        {
            var deadline = _deadlines.Peek();
            if (TryPeek(deadline, out _))
                return deadline.ReceivedAt + timeout;
            _deadlines.Dequeue();
        }

        return null;
    }

    private JoinPendingBucket<TLeft, TRight> GetOrCreateBucket(string key)
    {
        if (_pending.TryGetValue(key, out var bucket))
            return bucket;

        bucket = new JoinPendingBucket<TLeft, TRight>();
        _pending[key] = bucket;
        return bucket;
    }

    private bool TryPeek(
        JoinDeadline deadline,
        out JoinPendingBucket<TLeft, TRight> bucket)
    {
        if (!_pending.TryGetValue(deadline.Key, out bucket!))
            return false;

        return deadline.Side == FlowJoinSide.Left
            ? bucket.Lefts.Count > 0 && bucket.Lefts.Peek().ReceivedAt == deadline.ReceivedAt
            : bucket.Rights.Count > 0 && bucket.Rights.Peek().ReceivedAt == deadline.ReceivedAt;
    }

    private void RemoveIfEmpty(string key, JoinPendingBucket<TLeft, TRight> bucket)
    {
        if (bucket.Lefts.Count == 0 && bucket.Rights.Count == 0)
            _pending.Remove(key);
    }

    private sealed record JoinDeadline(
        string Key,
        FlowJoinSide Side,
        DateTimeOffset ReceivedAt);
}

internal sealed record JoinPendingEntry<T>(
    FlowMessage<T> Message,
    DateTimeOffset ReceivedAt);

internal sealed record ExpiredJoinEntry<TLeft, TRight>(
    string Key,
    FlowJoinSide Side,
    JoinPendingEntry<TLeft>? Left = null,
    JoinPendingEntry<TRight>? Right = null);

internal sealed class JoinPendingBucket<TLeft, TRight>
{
    internal Queue<JoinPendingEntry<TLeft>> Lefts { get; } = [];

    internal Queue<JoinPendingEntry<TRight>> Rights { get; } = [];
}
