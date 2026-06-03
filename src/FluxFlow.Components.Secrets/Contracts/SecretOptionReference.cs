namespace FluxFlow.Components.Secrets.Contracts;

public sealed record SecretOptionReference
{
    public required string OptionPath { get; init; }
    public SecretReference? Reference { get; init; }
    public bool Required { get; init; } = true;
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();

    public override string ToString()
        => Reference is null
            ? OptionPath
            : $"{OptionPath}: {Reference}";
}
