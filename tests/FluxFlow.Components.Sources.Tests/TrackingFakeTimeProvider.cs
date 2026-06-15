using Microsoft.Extensions.Time.Testing;

namespace FluxFlow.Components.Sources.Tests;

// FakeTimeProvider keeps Task.Delay pending until the clock is advanced, which
// inverts control flow for the source-timing tests: a test must advance the clock to
// let each pending delay fire. The catch is that the run loop arms its delays from a
// background loop, so advancing before the next timer is registered leaves the delay
// due past "now" forever (a hang under cross-assembly load). This wrapper tracks how
// many timers have been created so a test can deterministically wait until the loop
// has scheduled its next delay before advancing.
internal sealed class TrackingFakeTimeProvider : FakeTimeProvider
{
    private readonly object _gate = new();
    private int _createdCount;
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
            _createdCount++;
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

    // Total number of timers created so far. Monotonic; a fresh delay armed by the run
    // loop increments this. A test can advance once per increment to drain each delay
    // without racing the loop, even when a timer was registered before the test looked.
    public int CreatedTimerCount
    {
        get
        {
            lock (_gate)
            {
                return _createdCount;
            }
        }
    }

    private static TaskCompletionSource CreateSource()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
