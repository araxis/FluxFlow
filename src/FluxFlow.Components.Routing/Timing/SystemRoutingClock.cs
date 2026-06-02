namespace FluxFlow.Components.Routing.Timing;

public sealed class SystemRoutingClock : IRoutingClock
{
    public static SystemRoutingClock Instance { get; } = new();

    private SystemRoutingClock()
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
