using Microsoft.Extensions.Time.Testing;

namespace FluxFlow.Components.Routing.Tests;

// FakeTimeProvider keeps Task.Delay pending until the clock is advanced, which
// inverts control flow for the routing timeout tests: a test must advance the
// clock to let a timer fire. The catch is that advancing before the node has
// registered its timer leaves the delay due past "now" forever (a hang). This
// wrapper counts timer registrations so a test can deterministically wait until
// the node has scheduled its delay before advancing.
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

    // Captures the registration of the next timer. Call this BEFORE sending the
    // input that triggers the timer, then await the returned task to know the
    // node has scheduled its delay; only then is it safe to advance the clock.
    public Task NextTimerScheduled
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
