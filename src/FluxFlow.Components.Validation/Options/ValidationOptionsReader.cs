using FluxFlow.Engine.Definitions;
using System.Text.Json;

namespace FluxFlow.Components.Validation.Options;

internal static class ValidationOptionsReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static JsonSchemaValidatorOptions ReadJsonSchemaValidatorOptions(NodeDefinition definition)
    {
        var options = Read<JsonSchemaValidatorOptions>(definition);

        if (string.IsNullOrWhiteSpace(options.InputType))
        {
            throw new InvalidOperationException("json.schema-validator option 'inputType' cannot be empty.");
        }

        if (options.BoundedCapacity <= 0)
        {
            throw new InvalidOperationException(
                "json.schema-validator option 'boundedCapacity' must be greater than zero.");
        }

        return options;
    }

    private static T Read<T>(NodeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var json = JsonSerializer.Serialize(definition.Configuration, SerializerOptions);
        return JsonSerializer.Deserialize<T>(json, SerializerOptions)
            ?? throw new InvalidOperationException($"Could not read {typeof(T).Name}.");
    }
}
