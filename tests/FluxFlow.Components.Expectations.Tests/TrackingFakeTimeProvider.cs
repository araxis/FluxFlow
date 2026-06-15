using Microsoft.Extensions.Time.Testing;

namespace FluxFlow.Components.Expectations.Tests;

// FakeTimeProvider keeps Task.Delay pending until the clock is advanced, which
// inverts control flow for the expectation timeout tests: a test must advance the
// clock to let the timeout fire. The catch is that the node arms its timeout delay
// from a background loop, so advancing before that timer is registered leaves the
// delay due past "now" forever (a hang under cross-assembly load). This wrapper
// signals each timer registration so a test can deterministically wait until the
// node has scheduled its delay before advancing.
internal sealed class TrackingFakeTimeProvider : FakeTimeProvider
{
    private readonly object _gate = new();
    private TaskCompletionSource _nextTimer = CreateSource();

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
        var timer = base.CreateTimer(callback, state, dueTime, period);
        TaskCompletionSource signalled;
        lock (_gate)
        {
            signalled = _nextTimer;
            _nextTimer = CreateSource();
        }

        signalled.TrySetResult();
        return timer;
    }

    // Captures the registration of the next timer. Call this BEFORE the action that
    // arms the timer, then await the returned task to know the node has scheduled its
    // delay; only then is it safe to advance the clock.
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
}
