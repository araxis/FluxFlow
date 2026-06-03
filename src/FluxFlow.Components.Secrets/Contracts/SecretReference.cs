namespace FluxFlow.Components.Secrets.Contracts;

public sealed record SecretReference
{
    public required SecretName Name { get; init; }
    public string? Version { get; init; }
    public string? Kind { get; init; }
    public IReadOnlyDictionary<string, string> Attributes { get; init; } = new Dictionary<string, string>();

    public override string ToString()
        => string.IsNullOrWhiteSpace(Version)
            ? Name.ToString()
            : $"{Name}@{Version}";
}
