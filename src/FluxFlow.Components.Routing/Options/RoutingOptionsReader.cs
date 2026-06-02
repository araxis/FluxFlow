using FluxFlow.Engine.Definitions;
using System.Text.Json;

namespace FluxFlow.Components.Routing.Options;

internal static class RoutingOptionsReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static SwitchRoutingOptions ReadSwitchOptions(NodeDefinition definition)
    {
        var options = Read<SwitchRoutingOptions>(definition);
        if (string.IsNullOrWhiteSpace(options.Expression))
        {
            throw new InvalidOperationException("flow.switch requires configuration value 'expression'.");
        }

        if (string.IsNullOrWhiteSpace(options.InputType))
        {
            throw new InvalidOperationException("flow.switch option 'inputType' cannot be empty.");
        }

        if (options.BoundedCapacity <= 0)
        {
            throw new InvalidOperationException("flow.switch option 'boundedCapacity' must be greater than zero.");
        }

        if (options.Routes.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("flow.switch option 'routes' cannot contain empty values.");
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
