using FluxFlow.Engine.Definitions;
using System.Text.Json;

namespace FluxFlow.Components.Payloads.Options;

internal static class PayloadOptionsReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static PayloadInspectOptions ReadInspectOptions(NodeDefinition definition)
    {
        var options = Read<PayloadInspectOptions>(definition);

        if (options.MaxPreviewBytes <= 0)
        {
            throw new InvalidOperationException(
                "payload.inspect option 'maxPreviewBytes' must be greater than zero.");
        }

        if (options.MaxFormattedChars <= 0)
        {
            throw new InvalidOperationException(
                "payload.inspect option 'maxFormattedChars' must be greater than zero.");
        }

        if (options.BoundedCapacity <= 0)
        {
            throw new InvalidOperationException(
                "payload.inspect option 'boundedCapacity' must be greater than zero.");
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
