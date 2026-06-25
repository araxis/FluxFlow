namespace FluxFlow.Components.Metrics.Contracts;

public sealed record MetricSampleInput
{
    private string? _name;
    private string? _group;
    private string? _unit;
    private Dictionary<string, string> _tags = new(StringComparer.Ordinal);

    public DateTimeOffset? Timestamp { get; init; }

    public string? Name
    {
        get => _name;
        init => _name = MetricsContractNormalization.NormalizeOptional(value);
    }

    public string? Group
    {
        get => _group;
        init => _group = MetricsContractNormalization.NormalizeOptional(value);
    }

    public double? Value { get; init; }

    public string? Unit
    {
        get => _unit;
        init => _unit = MetricsContractNormalization.NormalizeOptional(value);
    }

    public long? Size { get; init; }

    public Dictionary<string, string> Tags
    {
        get => _tags;
        init => _tags = MetricsContractNormalization.CopyTags(value);
    }
}
