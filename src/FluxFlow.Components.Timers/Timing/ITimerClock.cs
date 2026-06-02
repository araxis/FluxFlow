namespace FluxFlow.Components.Timers.Timing;

public interface ITimerClock
{
    DateTimeOffset UtcNow { get; }

    ValueTask DelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken = default);
}
