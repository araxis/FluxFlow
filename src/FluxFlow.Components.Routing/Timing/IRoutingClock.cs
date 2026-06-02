namespace FluxFlow.Components.Routing.Timing;

public interface IRoutingClock
{
    DateTimeOffset UtcNow { get; }

    ValueTask DelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken = default);
}
