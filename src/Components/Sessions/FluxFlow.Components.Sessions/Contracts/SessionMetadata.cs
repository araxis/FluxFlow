namespace FluxFlow.Components.Sessions.Contracts;

public sealed record SessionMetadata
{
    private string _sessionId = string.Empty;
    private string? _name;
    private string? _notes;
    private Dictionary<string, string> _tags = new(StringComparer.Ordinal);

    public required string SessionId
    {
        get => _sessionId;
        init => _sessionId = SessionContractNormalization.NormalizeRequired(value);
    }

    public string? Name
    {
        get => _name;
        init => _name = SessionContractNormalization.NormalizeOptional(value);
    }

    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public long MessageCount { get; init; }

    public string? Notes
    {
        get => _notes;
        init => _notes = SessionContractNormalization.NormalizeOptional(value);
    }

    public Dictionary<string, string> Tags
    {
        get => _tags;
        init => _tags = SessionContractNormalization.CopyMap(value);
    }
}
