using FluxFlow.Engine.Definitions;
using System.Text.Json;

namespace FluxFlow.Components.Control.Options;

internal static class ControlOptionsReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static ControlExpressionOptions Read(NodeDefinition definition, string nodeType)
    {
        var options = Read<ControlExpressionOptions>(definition);

        if (string.IsNullOrWhiteSpace(options.Expression))
        {
            throw new InvalidOperationException($"{nodeType} requires configuration value 'expression'.");
        }

        if (string.IsNullOrWhiteSpace(options.InputType))
        {
            throw new InvalidOperationException($"{nodeType} option 'inputType' cannot be empty.");
        }

        if (options.BoundedCapacity <= 0)
        {
            throw new InvalidOperationException($"{nodeType} option 'boundedCapacity' must be greater than zero.");
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
