using System.Text.Json;

namespace FluxFlow.Composition;

public sealed record NodeDefinition
{
    public required string Type { get; init; }

    public Dictionary<string, JsonElement> Configuration { get; init; } =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Named references to resources owned by the host or adapter DI layer.
    /// Composition records the references but does not create or register resources.
    /// </summary>
    public Dictionary<string, string> Resources { get; init; } =
        new(StringComparer.Ordinal);
}
