using FluxFlow.Engine.Definitions;
using System.Text.Json;

namespace FluxFlow.Components.Mapping.Options;

internal static class MappingOptionsReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static MapperOptions ReadMapperOptions(NodeDefinition definition)
    {
        var options = Read<MapperOptions>(definition);

        if (string.IsNullOrWhiteSpace(options.Expression))
        {
            throw new InvalidOperationException("flow.mapper requires configuration value 'expression'.");
        }

        if (string.IsNullOrWhiteSpace(options.InputType))
        {
            throw new InvalidOperationException("flow.mapper option 'inputType' cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(options.EffectiveOutputType))
        {
            throw new InvalidOperationException("flow.mapper option 'outputType' cannot be empty.");
        }

        if (options.BoundedCapacity <= 0)
        {
            throw new InvalidOperationException("flow.mapper option 'boundedCapacity' must be greater than zero.");
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
