namespace FluxFlow.Components.Expectations.Timing;

public interface IExpectationClock
{
    DateTimeOffset UtcNow { get; }

    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default);
}
