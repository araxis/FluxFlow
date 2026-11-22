using System.Runtime.ExceptionServices;
using FluxFlow.Core.Messaging;

namespace FluxFlow.Core.Coordination;

/// <summary>Owns pending replies and their deadlines for one FlowRequestReply instance.</summary>
internal sealed class PendingRequests : IAsyncDisposable
{
    private static readonly TimeSpan MaxTimerDueTime = TimeSpan.FromMilliseconds(uint.MaxValue - 1d);
    private readonly object _gate = new();
    private readonly Dictionary<(CorrelationId Correlation, string Address), Request> _requests = [];
    private readonly PriorityQueue<(CorrelationId Correlation, string Address), long> _deadlines = new();
    private readonly int _capacity;
    private readonly TimeProvider _clock;
    private readonly ITimer _timer;
    private bool _stopped;
    private Exception? _timerFailure;
    private int _timerDisposed;

    public PendingRequests(int capacity, TimeProvider? clock)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
        _clock = clock ?? TimeProvider.System;
        _timer = _clock.CreateTimer(static state => ((PendingRequests)state!).Expire(), this,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public int Count { get { lock (_gate) return _requests.Count; } }

    public Task<FlowMessage> Add(
        (CorrelationId Correlation, string Address) key, TimeSpan timeout)
    {
        if (timeout != Timeout.InfiniteTimeSpan && timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive or infinite.");
        lock (_gate)
        {
            if (_stopped) throw new OperationCanceledException("Request/reply has stopped.");
            if (_requests.Count >= _capacity)
                throw new InvalidOperationException("The pending request capacity has been reached.");
            var now = _clock.GetUtcNow();
            if (timeout > DateTimeOffset.MaxValue - now)
                throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout exceeds the supported deadline range.");
            var request = new Request(timeout == Timeout.InfiniteTimeSpan ? null : now + timeout);
            _requests.Add(key, request);
            if (request.Deadline is { } deadline) _deadlines.Enqueue(key, deadline.UtcTicks);
            try { Schedule(now); }
            catch
            {
                // Scheduling drained the waiters, but this new task cannot reach its caller.
                _ = request.Completion.Task.Exception;
                throw;
            }
            return request.Completion.Task;
        }
    }

    public bool Complete((CorrelationId Correlation, string Address) key,
        FlowMessage? reply = null, Exception? error = null)
    {
        lock (_gate)
        {
            if (!_requests.Remove(key, out var request)) return false;
            var now = _clock.GetUtcNow();
            // Timer callbacks may be delayed: an elapsed deadline still wins over a reply.
            var expired = request.Deadline is { } deadline && deadline <= now;
            if (expired) request.Completion.TrySetException(TimeoutError());
            else if (error is not null) request.Completion.TrySetException(error);
            else request.Completion.TrySetResult(reply!);
            if (_deadlines.Count > (_requests.Count * 2) + 64)
            {
                _deadlines.Clear();
                foreach (var pending in _requests)
                    if (pending.Value.Deadline is { } due) _deadlines.Enqueue(pending.Key, due.UtcTicks);
            }
            Schedule(now);
            return !expired;
        }
    }

    public void Stop(Exception? error = null)
    {
        lock (_gate)
        {
            if (_stopped) return;
            Drain(error ?? new OperationCanceledException("Request/reply stopped before the request completed."));
            // Complete all waiters before potentially fallible timer cleanup.
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    private void Drain(Exception error)
    {
        _stopped = true;
        foreach (var request in _requests.Values) request.Completion.TrySetException(error);
        _requests.Clear();
        _deadlines.Clear();
    }

    private void Expire()
    {
        lock (_gate)
        {
            if (_stopped) return;
            var now = _clock.GetUtcNow();
            while (_deadlines.TryPeek(out var key, out var ticks) && ticks <= now.UtcTicks)
            {
                _deadlines.Dequeue();
                if (_requests.Remove(key, out var request)) request.Completion.TrySetException(TimeoutError());
            }
            try { Schedule(now); }
            catch (Exception error) when (ReferenceEquals(error, _timerFailure))
            {
                // Remaining requests have failed; disposal also reports the timer failure.
            }
        }
    }

    private void Schedule(DateTimeOffset now)
    {
        while (_deadlines.TryPeek(out var key, out _) && !_requests.ContainsKey(key))
            _deadlines.Dequeue();
        var due = Timeout.InfiniteTimeSpan;
        if (_deadlines.TryPeek(out _, out var ticks))
        {
            due = TimeSpan.FromTicks(Math.Max(0, ticks - now.UtcTicks));
            if (due > MaxTimerDueTime) due = MaxTimerDueTime;
        }
        try
        {
            if (!_timer.Change(due, Timeout.InfiniteTimeSpan))
                throw new InvalidOperationException("The pending request timer rejected scheduling.");
        }
        catch (Exception error)
        {
            _timerFailure = error;
            Drain(error);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Exception? failure = null;
        try { Stop(); }
        catch (Exception error) { failure = error; }
        try
        {
            if (Interlocked.Exchange(ref _timerDisposed, 1) == 0)
            {
                failure ??= _timerFailure;
                await _timer.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (Exception error) when (failure is not null)
        {
            throw new AggregateException(failure, error);
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static TimeoutException TimeoutError()
        => new("The request deadline expired during admission or reply waiting.");

    private sealed class Request(DateTimeOffset? deadline)
    {
        public DateTimeOffset? Deadline { get; } = deadline;
        public TaskCompletionSource<FlowMessage> Completion { get; }
            = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
