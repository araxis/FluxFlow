namespace FluxFlow.Components.Sessions.Contracts;

public sealed record SessionStartRequest
{
    private string? _sessionId;
    private string? _name;
    private string? _notes;
    private Dictionary<string, string> _tags = new(StringComparer.Ordinal);

    public string? SessionId
    {
        get => _sessionId;
        init => _sessionId = SessionContractNormalization.NormalizeOptional(value);
    }

    public string? Name
    {
        get => _name;
        init => _name = SessionContractNormalization.NormalizeOptional(value);
    }

    public required DateTimeOffset StartedAt { get; init; }

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
