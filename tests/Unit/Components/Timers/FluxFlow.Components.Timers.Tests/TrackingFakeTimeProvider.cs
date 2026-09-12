using Microsoft.Extensions.Time.Testing;

namespace FluxFlow.Components.Timers.Tests;

// FakeTimeProvider keeps Task.Delay pending until the clock is advanced, which
// inverts control flow for the timer-node tests: a test must advance the clock to
// let a pending delay fire. The catch is that the timer/interval loops arm their
// delays from a background loop, so advancing before the next timer is registered
// leaves the delay due past "now" forever (a hang under cross-assembly load). This
// wrapper signals each timer registration so a test can deterministically wait
// until the loop has scheduled its next delay before advancing.
internal sealed class TrackingFakeTimeProvider : FakeTimeProvider
{
    private readonly object _gate = new();
    private int _activeTimers;

    public int ActiveTimers => Volatile.Read(ref _activeTimers);
    private TaskCompletionSource _nextTimer = CreateSource();

    public TrackingFakeTimeProvider()
    {
    }

    public TrackingFakeTimeProvider(DateTimeOffset startDateTime)
        : base(startDateTime)
    {
    }

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        var inner = base.CreateTimer(callback, state, dueTime, period);
        Interlocked.Increment(ref _activeTimers);
        var timer = new TrackedTimer(inner, () => Interlocked.Decrement(ref _activeTimers));
        TaskCompletionSource signalled;
        lock (_gate)
        {
            signalled = _nextTimer;
            _nextTimer = CreateSource();
        }

        signalled.TrySetResult();
        return timer;
    }

    // Captures the registration of the next timer. Capture this BEFORE the action that
    // arms the next delay, then await the returned task to know the loop has scheduled
    // it; only then is it safe to advance the clock.
    public Task TimerScheduled
    {
        get
        {
            lock (_gate)
            {
                return _nextTimer.Task;
            }
        }
    }

    private static TaskCompletionSource CreateSource()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class TrackedTimer(ITimer inner, Action disposed) : ITimer
    {
        private int _disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period) => inner.Change(dueTime, period);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { inner.Dispose(); }
            finally { disposed(); }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { await inner.DisposeAsync(); }
            finally { disposed(); }
        }
    }
}
