using FluxFlow.Engine.Definitions;
using System.Text;
using System.Text.Json;

namespace FluxFlow.Components.Serialization.Options;

internal static class SerializationOptionsReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static SerializationNodeOptions ReadNodeOptions(
        NodeDefinition definition,
        string nodeType)
    {
        var options = Read<SerializationNodeOptions>(definition);

        if (options.BoundedCapacity <= 0)
        {
            throw new InvalidOperationException(
                $"{nodeType} option 'boundedCapacity' must be greater than zero.");
        }

        if (options.MaxInputBytes <= 0)
        {
            throw new InvalidOperationException(
                $"{nodeType} option 'maxInputBytes' must be greater than zero.");
        }

        if (options.MaxOutputBytes <= 0)
        {
            throw new InvalidOperationException(
                $"{nodeType} option 'maxOutputBytes' must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(options.DefaultEncoding))
        {
            throw new InvalidOperationException(
                $"{nodeType} option 'defaultEncoding' must not be empty.");
        }

        try
        {
            Encoding.GetEncoding(options.DefaultEncoding);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"{nodeType} option 'defaultEncoding' is not supported.",
                exception);
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
