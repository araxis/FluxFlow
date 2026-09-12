using Microsoft.Extensions.Time.Testing;

namespace FluxFlow.Core.Tests;

internal sealed class ApplicationCleanupTimeProvider : FakeTimeProvider
{
    public Exception? StopError { get; set; }
    public Exception? DisposeError { get; set; }
    public int DisposeCalls { get; private set; }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        => new CleanupTimer(this, base.CreateTimer(callback, state, dueTime, period));

    private sealed class CleanupTimer(ApplicationCleanupTimeProvider owner, ITimer inner) : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime == Timeout.InfiniteTimeSpan && owner.StopError is { } error) throw error;
            return inner.Change(dueTime, period);
        }

        public void Dispose() => inner.Dispose();

        public async ValueTask DisposeAsync()
        {
            owner.DisposeCalls++;
            await inner.DisposeAsync();
            if (owner.DisposeError is { } error) throw error;
        }
    }
}
