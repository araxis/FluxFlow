using System.Text.Json;

namespace FluxFlow.Json.Model;

public class ResourceDefinition
{
    private readonly JsonElement _value;

    public ResourceDefinition(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Undefined) throw new ArgumentException("Resource value cannot be undefined.", nameof(value));
        DefinitionRules.ValidateJsonValue(value, "Resources", nameof(value));
        _value = value.Clone();
    }

    public virtual JsonElement Value => _value;

    private protected ResourceDefinition()
    {
    }
}
