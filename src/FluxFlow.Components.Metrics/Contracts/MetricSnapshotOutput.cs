namespace FluxFlow.Components.Metrics.Contracts;

public sealed record MetricSnapshotOutput
{
    private string? _name;
    private string? _unit;
    private MetricSampleInput? _latest;
    private Dictionary<string, MetricGroupSnapshot> _groups = new(StringComparer.Ordinal);

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public string? Name
    {
        get => _name;
        init => _name = MetricsContractNormalization.NormalizeOptional(value);
    }

    public string? Unit
    {
        get => _unit;
        init => _unit = MetricsContractNormalization.NormalizeOptional(value);
    }

    public long SampleCount { get; init; }
    public long ValueCount { get; init; }
    public double? TotalValue { get; init; }
    public double? AverageValue { get; init; }
    public double? MinValue { get; init; }
    public double? MaxValue { get; init; }
    public double CurrentRate { get; init; }
    public double AverageRate { get; init; }
    public long? TotalSize { get; init; }

    public MetricSampleInput? Latest
    {
        get => _latest;
        init => _latest = MetricsContractNormalization.CopySample(value);
    }

    public Dictionary<string, MetricGroupSnapshot> Groups
    {
        get => _groups;
        init => _groups = MetricsContractNormalization.CopyGroups(value);
    }
}
