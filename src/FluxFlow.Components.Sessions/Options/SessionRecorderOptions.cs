namespace FluxFlow.Components.Sessions.Options;

public sealed record SessionRecorderOptions
{
    private string? _store;
    private string? _sessionId;
    private string? _name;
    private string? _notes;
    private Dictionary<string, string> _tags = new(StringComparer.Ordinal);
    private int _boundedCapacity = 128;

    public string? Store
    {
        get => _store;
        init => _store = SessionOptionValidation.Normalize(value);
    }

    public string? SessionId
    {
        get => _sessionId;
        init => _sessionId = SessionOptionValidation.Normalize(value);
    }

    public string? Name
    {
        get => _name;
        init => _name = SessionOptionValidation.Normalize(value);
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
