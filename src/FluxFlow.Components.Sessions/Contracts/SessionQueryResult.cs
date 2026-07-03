namespace FluxFlow.Components.Sessions.Contracts;

public sealed record SessionQueryResult
{
    private string _operation = string.Empty;
    private IReadOnlyList<SessionMetadata> _sessions = [];
    private string? _correlationId;
    private string? _message;
    private Dictionary<string, string> _attributes = new(StringComparer.Ordinal);

    public required DateTimeOffset Timestamp { get; init; }

    public required string Operation
    {
        get => _operation;
        init => _operation = SessionContractNormalization.NormalizeRequired(value);
    }

    public required bool Succeeded { get; init; }
    public required int Count { get; init; }

    public IReadOnlyList<SessionMetadata> Sessions
    {
        get => _sessions;
        init => _sessions = SessionContractNormalization.CopySessions(value);
    }

    public string? CorrelationId
    {
        get => _correlationId;
        init => _correlationId = SessionContractNormalization.NormalizeOptional(value);
    }

    public string? Message
    {
        get => _message;
        init => _message = SessionContractNormalization.NormalizeOptional(value);
    }

    public Dictionary<string, string> Attributes
    {
        get => _attributes;
        init => _attributes = SessionContractNormalization.CopyMap(value);
    }
}
