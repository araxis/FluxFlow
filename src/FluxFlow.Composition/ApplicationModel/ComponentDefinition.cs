using System.Text.Json;

namespace FluxFlow.Composition.Model;

public sealed class ComponentDefinition
{
    public ComponentDefinition(
        string type,
        IEnumerable<KeyValuePair<string, JsonElement>>? properties = null)
    {
        Type = DefinitionRules.RequireType(type, nameof(type));
        Properties = DefinitionRules.CopyProperties(
            properties,
            nameof(properties),
            rejectLegacyComponentWrappers: true);
    }

    public string Type { get; }

    public IReadOnlyDictionary<string, JsonElement> Properties { get; }
}
