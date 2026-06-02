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

        ValidateRouteOutputs(options);
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

    public static WindowRoutingOptions ReadWindowOptions(NodeDefinition definition)
    {
        var options = Read<WindowRoutingOptions>(definition);
        if (string.IsNullOrWhiteSpace(options.InputType))
        {
            throw new InvalidOperationException("flow.window option 'inputType' cannot be empty.");
        }

        if (options.MaxItems < 0)
        {
            throw new InvalidOperationException("flow.window option 'maxItems' cannot be negative.");
        }

        if (options.TimeMilliseconds < 0)
        {
            throw new InvalidOperationException(
                "flow.window option 'timeMilliseconds' cannot be negative.");
        }

        if (options.MaxItems == 0 && options.TimeMilliseconds == 0)
        {
            throw new InvalidOperationException(
                "flow.window requires option 'maxItems' or 'timeMilliseconds'.");
        }

        if (options.BoundedCapacity <= 0)
        {
            throw new InvalidOperationException(
                "flow.window option 'boundedCapacity' must be greater than zero.");
        }

        return options;
    }

    public static JoinRoutingOptions ReadJoinOptions(NodeDefinition definition)
    {
        var options = Read<JoinRoutingOptions>(definition);
        if (string.IsNullOrWhiteSpace(options.LeftKeyExpression))
        {
            throw new InvalidOperationException("flow.join requires configuration value 'leftKeyExpression'.");
        }

        if (string.IsNullOrWhiteSpace(options.RightKeyExpression))
        {
            throw new InvalidOperationException("flow.join requires configuration value 'rightKeyExpression'.");
        }

        if (string.IsNullOrWhiteSpace(options.LeftInputType))
        {
            throw new InvalidOperationException("flow.join option 'leftInputType' cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(options.RightInputType))
        {
            throw new InvalidOperationException("flow.join option 'rightInputType' cannot be empty.");
        }

        if (options.TimeoutMilliseconds <= 0)
        {
            throw new InvalidOperationException(
                "flow.join option 'timeoutMilliseconds' must be greater than zero.");
        }

        if (options.MaxPending <= 0)
        {
            throw new InvalidOperationException("flow.join option 'maxPending' must be greater than zero.");
        }

        if (options.BoundedCapacity <= 0)
        {
            throw new InvalidOperationException(
                "flow.join option 'boundedCapacity' must be greater than zero.");
        }

        return options;
    }

    public static ForkRoutingOptions ReadForkOptions(NodeDefinition definition)
    {
        var options = Read<ForkRoutingOptions>(definition);
        if (string.IsNullOrWhiteSpace(options.InputType))
        {
            throw new InvalidOperationException("flow.fork option 'inputType' cannot be empty.");
        }

        if (options.BoundedCapacity <= 0)
        {
            throw new InvalidOperationException("flow.fork option 'boundedCapacity' must be greater than zero.");
        }

        ValidatePortNames(
            "flow.fork",
            "outputs",
            options.Outputs,
            [
                RoutingComponentPorts.Input,
                RoutingComponentPorts.Errors
            ]);
        return options;
    }

    public static MergeRoutingOptions ReadMergeOptions(NodeDefinition definition)
    {
        var options = Read<MergeRoutingOptions>(definition);
        if (string.IsNullOrWhiteSpace(options.InputType))
        {
            throw new InvalidOperationException("flow.merge option 'inputType' cannot be empty.");
        }

        if (options.BoundedCapacity <= 0)
        {
            throw new InvalidOperationException("flow.merge option 'boundedCapacity' must be greater than zero.");
        }

        ValidatePortNames(
            "flow.merge",
            "inputs",
            options.Inputs,
            [
                RoutingComponentPorts.Output,
                RoutingComponentPorts.Errors
            ]);
        return options;
    }

    private static T Read<T>(NodeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var json = JsonSerializer.Serialize(definition.Configuration, SerializerOptions);
        return JsonSerializer.Deserialize<T>(json, SerializerOptions)
            ?? throw new InvalidOperationException($"Could not read {typeof(T).Name}.");
    }

    private static void ValidateRouteOutputs(SwitchRoutingOptions options)
    {
        if (options.RouteOutputs.Count == 0)
        {
            return;
        }

        var comparer = options.CaseSensitive
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;
        var routeKeys = options.Routes
            .Select(route => route.Trim())
            .ToHashSet(comparer);
        var seenRouteOutputs = new HashSet<string>(comparer);
        var builtInPorts = new HashSet<string>(
            [
                RoutingComponentPorts.Input,
                RoutingComponentPorts.Result,
                RoutingComponentPorts.Routed,
                RoutingComponentPorts.Matched,
                RoutingComponentPorts.Default,
                RoutingComponentPorts.Errors
            ],
            StringComparer.OrdinalIgnoreCase);

        foreach (var (routeKey, portName) in options.RouteOutputs)
        {
            if (string.IsNullOrWhiteSpace(routeKey))
            {
                throw new InvalidOperationException(
                    "flow.switch option 'routeOutputs' cannot contain empty route keys.");
            }

            var normalizedRoute = routeKey.Trim();
            if (!seenRouteOutputs.Add(normalizedRoute))
            {
                throw new InvalidOperationException(
                    $"flow.switch option 'routeOutputs' contains duplicate route key '{normalizedRoute}'.");
            }

            if (routeKeys.Count > 0 && !routeKeys.Contains(normalizedRoute))
            {
                throw new InvalidOperationException(
                    $"flow.switch route output '{normalizedRoute}' must also be present in 'routes'.");
            }

            if (string.IsNullOrWhiteSpace(portName))
            {
                throw new InvalidOperationException(
                    $"flow.switch route output '{normalizedRoute}' cannot use an empty port name.");
            }

            var normalizedPort = portName.Trim();
            if (builtInPorts.Contains(normalizedPort))
            {
                throw new InvalidOperationException(
                    $"flow.switch route output '{normalizedRoute}' cannot use built-in port '{normalizedPort}'.");
            }

            try
            {
                _ = new PortName(normalizedPort);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidOperationException(
                    $"flow.switch route output '{normalizedRoute}' has invalid port '{normalizedPort}'.",
                    exception);
            }
        }
    }

    private static void ValidatePortNames(
        string nodeType,
        string optionName,
        IReadOnlyCollection<string> portNames,
        IReadOnlyCollection<string> reservedPorts)
    {
        if (portNames.Count == 0)
        {
            throw new InvalidOperationException(
                $"{nodeType} option '{optionName}' must contain at least one value.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reserved = reservedPorts.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var portName in portNames)
        {
            if (string.IsNullOrWhiteSpace(portName))
            {
                throw new InvalidOperationException(
                    $"{nodeType} option '{optionName}' cannot contain empty values.");
            }

            var normalized = portName.Trim();
            if (!seen.Add(normalized))
            {
                throw new InvalidOperationException(
                    $"{nodeType} option '{optionName}' contains duplicate port '{normalized}'.");
            }

            if (reserved.Contains(normalized))
            {
                throw new InvalidOperationException(
                    $"{nodeType} option '{optionName}' cannot use built-in port '{normalized}'.");
            }

            try
            {
                _ = new PortName(normalized);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidOperationException(
                    $"{nodeType} option '{optionName}' contains invalid port '{normalized}'.",
                    exception);
            }
        }
    }
}
