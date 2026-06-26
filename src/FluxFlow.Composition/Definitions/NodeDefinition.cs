using System.Text.Json;

namespace FluxFlow.Composition;

public sealed record NodeDefinition
{
    private Dictionary<string, JsonElement> _configuration = new(StringComparer.Ordinal);
    private Dictionary<string, string> _resources = new(StringComparer.Ordinal);
    private string _type = string.Empty;

    public required string Type
    {
        get => _type;
        init => _type = value?.Trim() ?? string.Empty;
    }

    public Dictionary<string, JsonElement> Configuration
    {
        get => _configuration;
        init => _configuration = value is null
            ? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            : new Dictionary<string, JsonElement>(value, StringComparer.Ordinal);
    }

    /// <summary>
    /// Named references to resources owned by the host or adapter DI layer.
    /// Composition records the references but does not create or register resources.
    /// </summary>
    public Dictionary<string, string> Resources
    {
        get => _resources;
        init => _resources = value is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(value, StringComparer.Ordinal);
    }
}
