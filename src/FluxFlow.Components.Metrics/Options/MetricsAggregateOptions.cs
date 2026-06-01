namespace FluxFlow.Components.Metrics.Options;

public sealed record MetricsAggregateOptions
{
    public double RateWindowSeconds { get; init; } = 60;
    public int BoundedCapacity { get; init; } = 128;
    public int MaxGroups { get; init; } = 1024;
    public bool EmitEverySample { get; init; } = true;
    public bool TrackLatest { get; init; } = true;
    public bool TrackMinMax { get; init; } = true;
    public bool TrackSize { get; init; } = true;
    public string? GroupByTag { get; init; }
    public bool TreatMissingValueAsZero { get; init; }
}
