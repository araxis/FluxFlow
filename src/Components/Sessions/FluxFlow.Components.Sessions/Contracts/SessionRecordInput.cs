namespace FluxFlow.Components.Sessions.Contracts;

public sealed record SessionRecordInput
{
    private string? _type;
    private string? _name;
    private string? _contentType;
    private Dictionary<string, string> _attributes = new(StringComparer.Ordinal);

    public DateTimeOffset? Timestamp { get; init; }

    public string? Type
    {
        get => _type;
        init => _type = SessionContractNormalization.NormalizeOptional(value);
    }

    public string? Name
    {
        get => _name;
        init => _name = SessionContractNormalization.NormalizeOptional(value);
    }

    public object? Payload { get; init; }

    public string? ContentType
    {
        get => _contentType;
        init => _contentType = SessionContractNormalization.NormalizeOptional(value);
    }

    public Dictionary<string, string> Attributes
    {
        get => _attributes;
        init => _attributes = SessionContractNormalization.CopyMap(value);
    }
}
