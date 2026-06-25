namespace FluxFlow.Components.Resources.Contracts;

public sealed record ResourceReference
{
    private string? _kind;

    public required ResourceName Name { get; init; }
    public string? Kind
    {
        get => _kind;
        init => _kind = value?.Trim();
    }

    public IReadOnlyDictionary<string, string> Attributes { get; init; } = new Dictionary<string, string>();
}
