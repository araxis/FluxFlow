namespace FluxFlow.Components.Secrets.Contracts;

public sealed record SecretReference
{
    private string? _version;
    private string? _kind;

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

    public IReadOnlyDictionary<string, string> Attributes { get; init; } = new Dictionary<string, string>();

    public override string ToString()
        => string.IsNullOrWhiteSpace(Version)
            ? Name.ToString()
            : $"{Name}@{Version}";
}
