using System.Text.Json;

namespace FluxFlow.Components.Designer.Persistence;

public sealed record DesignerComponent
{
    public required string Type { get; init; }

    public IReadOnlyDictionary<string, JsonElement> Properties { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}
