using FluxFlow.Components.Resources.Contracts;

namespace FluxFlow.Components.Configuration.Contracts;

public sealed record ConfigurationResourceReference
{
    public required string Path { get; init; }
    public ResourceReference? Reference { get; init; }
    public bool Required { get; init; } = true;
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
