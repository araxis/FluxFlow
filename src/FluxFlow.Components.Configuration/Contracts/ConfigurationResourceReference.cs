using FluxFlow.Components.Resources.Contracts;

namespace FluxFlow.Components.Configuration.Contracts;

public sealed record ConfigurationResourceReference
{
    private string _path = string.Empty;
    private IReadOnlyDictionary<string, string>? _metadata = new Dictionary<string, string>();

    public required string Path
    {
        get => _path;
        init => _path = value?.Trim() ?? string.Empty;
    }

    public ResourceReference? Reference { get; init; }
    public bool Required { get; init; } = true;
    public IReadOnlyDictionary<string, string> Metadata
    {
        get => _metadata!;
        init => _metadata = ConfigurationContractMap.NormalizeOrPreserveInvalid(value);
    }
}
