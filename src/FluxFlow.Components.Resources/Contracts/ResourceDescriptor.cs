namespace FluxFlow.Components.Resources.Contracts;

public sealed record ResourceDescriptor
{
    private string? _kind;
    private string? _displayName;
    private string? _summary;
    private IReadOnlyDictionary<string, string>? _metadata = new Dictionary<string, string>();

    public required ResourceName Name { get; init; }
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

    public IReadOnlyDictionary<string, string> Metadata
    {
        get => _metadata!;
        init => _metadata = ResourceContractMap.NormalizeOrPreserveInvalid(value);
    }
}
