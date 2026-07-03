namespace FluxFlow.Components.Resources.Contracts;

public sealed record ResourceReference
{
    private string? _kind;
    private IReadOnlyDictionary<string, string>? _attributes = new Dictionary<string, string>();

    public required ResourceName Name { get; init; }
    public string? Kind
    {
        get => _kind;
        init => _kind = value?.Trim();
    }

    public IReadOnlyDictionary<string, string> Attributes
    {
        get => _attributes!;
        init => _attributes = ResourceContractMap.NormalizeOrPreserveInvalid(value);
    }
}
