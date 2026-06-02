namespace FluxFlow.Components.Sources.Timing;

public sealed class SystemSourceClock : ISourceClock
{
    public static SystemSourceClock Instance { get; } = new();

    private SystemSourceClock()
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
