namespace FluxFlow.Components.Metrics.Options;

public sealed class MetricsComponentOptions
{
    private TimeProvider _clock = TimeProvider.System;

    public TimeProvider Clock => _clock;

    public MetricsComponentOptions UseClock(TimeProvider clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        return this;
    }
}
