namespace FluxFlow.Components.Sessions.Timing;

public sealed class SystemSessionClock : ISessionClock
{
    public static SystemSessionClock Instance { get; } = new();

    private SystemSessionClock()
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
