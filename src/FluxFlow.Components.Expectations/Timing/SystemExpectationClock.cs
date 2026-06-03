namespace FluxFlow.Components.Expectations.Timing;

public sealed class SystemExpectationClock : IExpectationClock
{
    public static readonly SystemExpectationClock Instance = new();

    private SystemExpectationClock()
    {
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
        => Task.Delay(delay, cancellationToken);
}
