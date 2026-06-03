namespace FluxFlow.Components.Secrets.Contracts;

public sealed record SecretDescriptor
{
    public required SecretName Name { get; init; }
    public string? Version { get; init; }
    public string? Kind { get; init; }
    public string? DisplayName { get; init; }
    public string? Summary { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();

    public override string ToString()
        => string.IsNullOrWhiteSpace(Version)
            ? Name.ToString()
            : $"{Name}@{Version}";
}
