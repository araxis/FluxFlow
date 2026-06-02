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

    public static CorrelationRoutingOptions ReadCorrelationOptions(NodeDefinition definition)
    {
        var options = Read<CorrelationRoutingOptions>(definition);
        if (string.IsNullOrWhiteSpace(options.KeyExpression))
        {
            throw new InvalidOperationException("flow.correlation requires configuration value 'keyExpression'.");
        }

        if (string.IsNullOrWhiteSpace(options.SideExpression))
        {
            throw new InvalidOperationException("flow.correlation requires configuration value 'sideExpression'.");
        }

        if (string.IsNullOrWhiteSpace(options.InputType))
        {
            throw new InvalidOperationException("flow.correlation option 'inputType' cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(options.RequestSide))
        {
            throw new InvalidOperationException("flow.correlation option 'requestSide' cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(options.ResponseSide))
        {
            throw new InvalidOperationException("flow.correlation option 'responseSide' cannot be empty.");
        }

        var comparer = options.CaseSensitive
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;
        if (comparer.Equals(options.RequestSide.Trim(), options.ResponseSide.Trim()))
        {
            throw new InvalidOperationException(
                "flow.correlation options 'requestSide' and 'responseSide' must be different.");
        }

        if (options.TimeoutMilliseconds <= 0)
        {
            throw new InvalidOperationException(
                "flow.correlation option 'timeoutMilliseconds' must be greater than zero.");
        }

        if (options.MaxPending <= 0)
        {
            throw new InvalidOperationException("flow.correlation option 'maxPending' must be greater than zero.");
        }

        if (options.BoundedCapacity <= 0)
        {
            throw new InvalidOperationException(
                "flow.correlation option 'boundedCapacity' must be greater than zero.");
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
