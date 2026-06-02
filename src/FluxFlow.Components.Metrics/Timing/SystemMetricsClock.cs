namespace FluxFlow.Components.Metrics.Timing;

public sealed class SystemMetricsClock : IMetricsClock
{
    public static SystemMetricsClock Instance { get; } = new();

    private SystemMetricsClock()
    {
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
