namespace FluxFlow.Components.Metrics.Options;

public sealed record MetricsAggregateOptions
{
    private double _rateWindowSeconds = 60;
    private int _boundedCapacity = 128;
    private int _maxGroups = 1024;
    private string? _groupByTag;

    public double RateWindowSeconds
    {
        get => _rateWindowSeconds;
        init => _rateWindowSeconds = MetricsOptionValidation.ValidateRateWindowSeconds(value);
    }

    public int BoundedCapacity
    {
        get => _boundedCapacity;
        init => _boundedCapacity = MetricsOptionValidation.ValidateBoundedCapacity(value);
    }

    public int MaxGroups
    {
        get => _maxGroups;
        init => _maxGroups = MetricsOptionValidation.ValidateMaxGroups(value);
    }

    public bool EmitEverySample { get; init; } = true;
    public bool TrackLatest { get; init; } = true;
    public bool TrackMinMax { get; init; } = true;
    public bool TrackSize { get; init; } = true;

    public string? GroupByTag
    {
        get => _groupByTag;
        init => _groupByTag = MetricsOptionValidation.NormalizeOptional(value);
    }

    public bool TreatMissingValueAsZero { get; init; }
}
