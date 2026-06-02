namespace FluxFlow.Components.Metrics.Timing;

public interface IMetricsClock
{
    DateTimeOffset UtcNow { get; }
}
