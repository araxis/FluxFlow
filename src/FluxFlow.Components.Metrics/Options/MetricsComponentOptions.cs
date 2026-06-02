using FluxFlow.Components.Metrics.Timing;

namespace FluxFlow.Components.Metrics.Options;

public sealed class MetricsComponentOptions
{
    private IMetricsClock _clock = SystemMetricsClock.Instance;

    public IMetricsClock Clock => _clock;

    public MetricsComponentOptions UseClock(IMetricsClock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        return this;
    }
}
