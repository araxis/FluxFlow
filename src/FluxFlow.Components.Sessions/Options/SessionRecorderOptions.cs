namespace FluxFlow.Components.Sessions.Options;

public sealed record SessionRecorderOptions
{
    private string? _sessionId;
    private string? _sessionName;
    private string? _notes;
    private Dictionary<string, string> _tags = new(StringComparer.Ordinal);
    private int _boundedCapacity = 128;

    public string? SessionId
    {
        get => _sessionId;
        init => _sessionId = SessionOptionValidation.Normalize(value);
    }

    public string? SessionName
    {
        get => _sessionName;
        init => _sessionName = SessionOptionValidation.Normalize(value);
    }

    public string? Notes
    {
        get => _notes;
        init => _notes = SessionOptionValidation.Normalize(value);
    }

    public Dictionary<string, string> Tags
    {
        get => _tags;
        init => _tags = SessionOptionValidation.CopyMap(value);
    }

    public int BoundedCapacity
    {
        get => _boundedCapacity;
        init => _boundedCapacity = SessionOptionValidation.ValidateBoundedCapacity(value);
    }
}
