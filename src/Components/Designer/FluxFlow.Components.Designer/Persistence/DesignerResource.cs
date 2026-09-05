using System.Text.Json;
using FluxFlow.Composition.Addressing;

namespace FluxFlow.Components.Designer.Persistence;

public sealed record DesignerResource : DesignerResourceNode
{
    public required ApplicationAddress Address { get; init; }

    public required string Type { get; init; }

    public IReadOnlyDictionary<string, JsonElement> Properties { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}
