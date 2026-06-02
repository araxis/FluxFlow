using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Engine.Mapping;
using System.Globalization;

namespace FluxFlow.Components.Routing.Nodes;

internal static class RoutingNodeSupport
{
    public static string? EvaluateRouteKey(
        IFlowExpressionEngine expressionEngine,
        SwitchRoutingOptions options,
        IRoutingContextFactory contextFactory,
        RoutingNodeContext nodeContext,
        object? input)
    {
        var context = contextFactory.Create(input, nodeContext);
        var value = expressionEngine.Evaluate(options.Expression!, context, typeof(object));
        return NormalizeRouteKey(value);
    }

    public static HashSet<string> CreateRouteSet(SwitchRoutingOptions options)
    {
        var comparer = options.CaseSensitive
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;
        return options.Routes
            .Select(route => route.Trim())
            .Where(route => route.Length > 0)
            .ToHashSet(comparer);
    }

    public static Dictionary<string, string> CreateRouteOutputPortMap(SwitchRoutingOptions options)
    {
        var comparer = options.CaseSensitive
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;
        return options.RouteOutputs
            .Select(routeOutput => new
            {
                Route = routeOutput.Key.Trim(),
                Port = routeOutput.Value.Trim()
            })
            .ToDictionary(routeOutput => routeOutput.Route, routeOutput => routeOutput.Port, comparer);
    }

    public static Dictionary<string, object?> CreateAttributes(
        SwitchRoutingOptions options,
        IFlowExpressionEngine expressionEngine,
        string? routeKey = null,
        bool? matched = null)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["inputType"] = options.InputType,
            ["engine"] = expressionEngine.Name,
            ["routes"] = options.Routes.Length,
            ["routeOutputs"] = options.RouteOutputs.Count,
            ["caseSensitive"] = options.CaseSensitive
        };

        if (!string.IsNullOrWhiteSpace(routeKey))
        {
            attributes["routeKey"] = routeKey;
        }

        if (matched.HasValue)
        {
            attributes["matched"] = matched.Value;
        }

        if (!string.IsNullOrWhiteSpace(options.DefaultRoute))
        {
            attributes["defaultRoute"] = options.DefaultRoute;
        }

        if (!string.IsNullOrWhiteSpace(options.ExpressionId))
        {
            attributes["expressionId"] = options.ExpressionId;
        }

        if (!string.IsNullOrWhiteSpace(options.ExpressionName))
        {
            attributes["expressionName"] = options.ExpressionName;
        }

        return attributes;
    }

    public static string CreateErrorContext(
        SwitchRoutingOptions options,
        IFlowExpressionEngine expressionEngine)
    {
        var values = new List<string>
        {
            $"inputType={options.InputType}",
            $"engine={expressionEngine.Name}",
            $"routes={options.Routes.Length}",
            $"routeOutputs={options.RouteOutputs.Count}"
        };

        if (!string.IsNullOrWhiteSpace(options.ExpressionId))
        {
            values.Add($"expressionId={options.ExpressionId}");
        }

        if (!string.IsNullOrWhiteSpace(options.ExpressionName))
        {
            values.Add($"expressionName={options.ExpressionName}");
        }

        return string.Join("; ", values);
    }

    private static string? NormalizeRouteKey(object? value)
        => value switch
        {
            null => null,
            string text => string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
            IFormattable formattable => NormalizeString(formattable.ToString(null, CultureInfo.InvariantCulture)),
            _ => NormalizeString(value.ToString())
        };

    private static string? NormalizeString(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
