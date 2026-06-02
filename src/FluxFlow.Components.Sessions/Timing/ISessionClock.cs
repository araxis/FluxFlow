namespace FluxFlow.Components.Sessions.Timing;

public interface ISessionClock
{
    DateTimeOffset UtcNow { get; }

    ValueTask DelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken = default);
}
