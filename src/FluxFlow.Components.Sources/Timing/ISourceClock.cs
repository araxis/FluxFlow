namespace FluxFlow.Components.Sources.Timing;

public interface ISourceClock
{
    DateTimeOffset UtcNow { get; }

    ValueTask DelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken = default);
}
