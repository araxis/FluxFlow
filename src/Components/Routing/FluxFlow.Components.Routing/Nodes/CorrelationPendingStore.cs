using FluxFlow.Nodes;

namespace FluxFlow.Components.Routing.Nodes;

internal sealed class CorrelationPendingStore<T>
{
    private readonly Dictionary<string, CorrelationPendingPair<T>> _pending;
    private readonly Queue<CorrelationDeadline> _deadlines = new();
    private readonly int _maxPending;

    internal CorrelationPendingStore(StringComparer comparer, int maxPending)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        if (maxPending <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPending));

        _pending = new Dictionary<string, CorrelationPendingPair<T>>(comparer);
        _maxPending = maxPending;
    }

    internal int Count => _pending.Count;

    internal bool TryGetOrCreate(
        string key,
        out CorrelationPendingPair<T> pending,
        out bool created)
    {
        created = false;
        if (_pending.TryGetValue(key, out pending!))
            return true;

        if (_pending.Count >= _maxPending)
        {
            pending = default!;
            return false;
        }

        pending = new CorrelationPendingPair<T>();
        _pending[key] = pending;
        created = true;
        return true;
    }

    internal void TrackDeadline(string key, DateTimeOffset receivedAt)
        => _deadlines.Enqueue(new CorrelationDeadline(key, receivedAt));

    internal bool Remove(string key) => _pending.Remove(key);

    internal IReadOnlyList<ExpiredCorrelation<T>> TakeExpired(
        DateTimeOffset now,
        TimeSpan timeout,
        bool force)
    {
        if (_pending.Count == 0)
        {
            _deadlines.Clear();
            return [];
        }

        var expired = new List<ExpiredCorrelation<T>>();
        while (_deadlines.Count > 0)
        {
            var deadline = _deadlines.Peek();
            if (!_pending.TryGetValue(deadline.Key, out var pending) ||
                pending.ReceivedAt != deadline.ReceivedAt)
            {
                _deadlines.Dequeue();
                continue;
            }

            if (!force && now - deadline.ReceivedAt < timeout)
                break;

            _deadlines.Dequeue();
            _pending.Remove(deadline.Key);
            expired.Add(new ExpiredCorrelation<T>(deadline.Key, pending));
        }

        return expired;
    }

    internal DateTimeOffset? GetNextDueAt(TimeSpan timeout)
    {
        while (_deadlines.Count > 0)
        {
            var deadline = _deadlines.Peek();
            if (_pending.TryGetValue(deadline.Key, out var pending) &&
                pending.ReceivedAt == deadline.ReceivedAt)
            {
                return deadline.ReceivedAt + timeout;
            }

            _deadlines.Dequeue();
        }

        return null;
    }

    private sealed record CorrelationDeadline(
        string Key,
        DateTimeOffset ReceivedAt);
}

internal sealed record ExpiredCorrelation<T>(
    string Key,
    CorrelationPendingPair<T> Pending);

internal sealed record CorrelationPendingEntry<T>(
    FlowMessage<T> Message,
    string Side,
    DateTimeOffset ReceivedAt);

internal sealed class CorrelationPendingPair<T>
{
    internal CorrelationPendingEntry<T>? Request { get; private set; }

    internal CorrelationPendingEntry<T>? Response { get; private set; }

    internal DateTimeOffset? ReceivedAt
        => Request?.ReceivedAt ?? Response?.ReceivedAt;

    internal IEnumerable<CorrelationPendingEntry<T>> Entries
    {
        get
        {
            if (Request is not null)
                yield return Request;
            if (Response is not null)
                yield return Response;
        }
    }

    internal CorrelationPendingEntry<T>? Get(string side, StringComparer comparer)
        => Request is not null && comparer.Equals(Request.Side, side)
            ? Request
            : Response is not null && comparer.Equals(Response.Side, side)
                ? Response
                : null;

    internal void Set(
        string side,
        CorrelationPendingEntry<T> entry,
        string requestSide,
        StringComparer comparer)
    {
        if (comparer.Equals(side, requestSide))
        {
            Request = entry;
            return;
        }

        Response = entry;
    }
}
