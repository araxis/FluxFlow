namespace FluxFlow.Components.Timers.Timing;

public sealed class SystemTimerClock : ITimerClock
{
    public static SystemTimerClock Instance { get; } = new();

    private SystemTimerClock()
    {
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public ValueTask DelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken = default)
    {
        if (delay <= TimeSpan.Zero)
        {
            return ValueTask.CompletedTask;
        }

        return new ValueTask(Task.Delay(delay, cancellationToken));
    }
}
