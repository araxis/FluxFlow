namespace FluxFlow.Components.Sessions.Contracts;

public sealed record SessionQueryRequest
{
    private string? _name;
    private string? _namePrefix;
    private Dictionary<string, string> _tags = new(StringComparer.Ordinal);
    private string? _correlationId;

    public string? Name
    {
        get => _name;
        init => _name = SessionContractNormalization.NormalizeOptional(value);
    }

    public string? NamePrefix
    {
        get => _namePrefix;
        init => _namePrefix = SessionContractNormalization.NormalizeOptional(value);
    }

    public Dictionary<string, string> Tags
    {
        get => _tags;
        init => _tags = SessionContractNormalization.CopyMap(value);
    }

    public DateTimeOffset? StartedFrom { get; init; }
    public DateTimeOffset? StartedTo { get; init; }
    public DateTimeOffset? EndedFrom { get; init; }
    public DateTimeOffset? EndedTo { get; init; }
    public bool? IncludeActive { get; init; }
    public bool? IncludeCompleted { get; init; }
    public int? Limit { get; init; }

    public string? CorrelationId
    {
        get => _correlationId;
        init => _correlationId = SessionContractNormalization.NormalizeOptional(value);
    }
}
