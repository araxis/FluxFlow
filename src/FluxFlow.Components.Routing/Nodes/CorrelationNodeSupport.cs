using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Engine.Mapping;
using System.Globalization;

namespace FluxFlow.Components.Routing.Nodes;

internal static class CorrelationNodeSupport
{
    public static string? EvaluateKey(
        IFlowExpressionEngine expressionEngine,
        CorrelationRoutingOptions options,
        IRoutingContextFactory contextFactory,
        RoutingNodeContext nodeContext,
        object? input)
        => EvaluateText(
            expressionEngine,
            options.KeyExpression!,
            contextFactory,
            nodeContext,
            input);

    public static string? EvaluateSide(
        IFlowExpressionEngine expressionEngine,
        CorrelationRoutingOptions options,
        IRoutingContextFactory contextFactory,
        RoutingNodeContext nodeContext,
        object? input)
        => EvaluateText(
            expressionEngine,
            options.SideExpression!,
            contextFactory,
            nodeContext,
            input);

    public static Dictionary<string, object?> CreateAttributes(
        CorrelationRoutingOptions options,
        IFlowExpressionEngine expressionEngine,
        int pendingCount,
        string? key = null,
        string? side = null)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["inputType"] = options.InputType,
            ["engine"] = expressionEngine.Name,
            ["caseSensitive"] = options.CaseSensitive,
            ["timeoutMilliseconds"] = options.TimeoutMilliseconds,
            ["maxPending"] = options.MaxPending,
            ["pendingCount"] = pendingCount
        };

        if (!string.IsNullOrWhiteSpace(key))
        {
            attributes["key"] = key;
        }

        if (!string.IsNullOrWhiteSpace(side))
        {
            attributes["side"] = side;
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
        CorrelationRoutingOptions options,
        IFlowExpressionEngine expressionEngine,
        string? key = null,
        string? side = null)
    {
        var values = new List<string>
        {
            $"inputType={options.InputType}",
            $"engine={expressionEngine.Name}",
            $"timeoutMilliseconds={options.TimeoutMilliseconds}",
            $"maxPending={options.MaxPending}"
        };

        if (!string.IsNullOrWhiteSpace(key))
        {
            values.Add($"key={key}");
        }

        if (!string.IsNullOrWhiteSpace(side))
        {
            values.Add($"side={side}");
        }

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

    private static string? EvaluateText(
        IFlowExpressionEngine expressionEngine,
        string expression,
        IRoutingContextFactory contextFactory,
        RoutingNodeContext nodeContext,
        object? input)
    {
        var context = contextFactory.Create(input, nodeContext);
        var value = expressionEngine.Evaluate(expression, context, typeof(object));
        return NormalizeText(value);
    }

    private static string? NormalizeText(object? value)
        => value switch
        {
            null => null,
            string text => NormalizeString(text),
            IFormattable formattable => NormalizeString(formattable.ToString(null, CultureInfo.InvariantCulture)),
            _ => NormalizeString(value.ToString())
        };

    private static string? NormalizeString(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
