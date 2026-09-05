namespace FluxFlow.Components.Routing.Tests;

// FakeTimeProvider cannot throw from its time/delay path, so the clock-fault
// tests use this bespoke provider. GetUtcNow() throws once (the first call) and
// returns a fixed time afterward, reproducing the old ThrowOnceRoutingClock. Its
// timers never fire, matching the old infinite-delay behaviour so the node only
// makes progress via its count-based boundary.
internal sealed class ThrowingTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _utcNow = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    private int _calls;

    public override DateTimeOffset GetUtcNow()
    {
        if (Interlocked.Increment(ref _calls) == 1)
        {
            throw new InvalidOperationException("clock failed.");
        }

        return _utcNow;
    }

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
        => new NeverFiringTimer();

    private sealed class NeverFiringTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
