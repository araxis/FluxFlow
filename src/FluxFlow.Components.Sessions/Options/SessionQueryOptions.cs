namespace FluxFlow.Components.Sessions.Options;

public sealed record SessionQueryOptions
{
    private string? _store;
    private string? _name;
    private string? _namePrefix;
    private Dictionary<string, string> _tags = new(StringComparer.Ordinal);
    private int _limit = 100;
    private int _boundedCapacity = 128;

    public string? Store
    {
        get => _store;
        init => _store = SessionOptionValidation.Normalize(value);
    }

    public string? Name
    {
        get => _name;
        init => _name = SessionOptionValidation.Normalize(value);
    }

    public string? NamePrefix
    {
        get => _namePrefix;
        init => _namePrefix = SessionOptionValidation.Normalize(value);
    }

    public Dictionary<string, string> Tags
    {
        get => _tags;
        init => _tags = SessionOptionValidation.CopyMap(value);
    }

    public bool IncludeActive { get; init; } = true;
    public bool IncludeCompleted { get; init; } = true;

    public int Limit
    {
        get => _limit;
        init => _limit = SessionOptionValidation.ValidateLimit(value);
    }

    public bool EmitSessionsInResult { get; init; } = true;
    public bool EmitSessionOutputs { get; init; } = true;

    public int BoundedCapacity
    {
        get => _boundedCapacity;
        init => _boundedCapacity = SessionOptionValidation.ValidateBoundedCapacity(value);
    }
}
