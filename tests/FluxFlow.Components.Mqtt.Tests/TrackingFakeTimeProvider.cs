using Microsoft.Extensions.Time.Testing;

namespace FluxFlow.Components.Mqtt.Tests;

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
        TaskCompletionSource scheduled;
        lock (_gate)
        {
            scheduled = _nextTimer;
            _nextTimer = CreateSource();
        }

        scheduled.TrySetResult();
        return timer;
    }

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
