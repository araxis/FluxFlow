namespace FluxFlow.Components.Secrets.Contracts;

public sealed record SecretDescriptor
{
    private string? _version;
    private string? _kind;
    private string? _displayName;
    private string? _summary;

    public required SecretName Name { get; init; }
    public string? Version
    {
        get => _version;
        init => _version = value?.Trim();
    }

    public string? Kind
    {
        get => _kind;
        init => _kind = value?.Trim();
    }

    public string? DisplayName
    {
        get => _displayName;
        init => _displayName = value?.Trim();
    }

    public string? Summary
    {
        get => _summary;
        init => _summary = value?.Trim();
    }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();

    public override string ToString()
        => string.IsNullOrWhiteSpace(Version)
            ? Name.ToString()
            : $"{Name}@{Version}";
}
