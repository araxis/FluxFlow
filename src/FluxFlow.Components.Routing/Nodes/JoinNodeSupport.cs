using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Options;
using FluxFlow.Engine.Mapping;
using System.Globalization;

namespace FluxFlow.Components.Routing.Nodes;

internal static class JoinNodeSupport
{
    public static string? EvaluateLeftKey(
        IFlowExpressionEngine expressionEngine,
        JoinRoutingOptions options,
        IRoutingContextFactory contextFactory,
        RoutingNodeContext nodeContext,
        object? input)
        => EvaluateText(
            expressionEngine,
            options.LeftKeyExpression!,
            contextFactory,
            nodeContext,
            input);

    public static string? EvaluateRightKey(
        IFlowExpressionEngine expressionEngine,
        JoinRoutingOptions options,
        IRoutingContextFactory contextFactory,
        RoutingNodeContext nodeContext,
        object? input)
        => EvaluateText(
            expressionEngine,
            options.RightKeyExpression!,
            contextFactory,
            nodeContext,
            input);

    public static Dictionary<string, object?> CreateAttributes(
        JoinRoutingOptions options,
        IFlowExpressionEngine expressionEngine,
        int pendingCount,
        string? key = null,
        FlowJoinSide? side = null)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["leftInputType"] = options.LeftInputType,
            ["rightInputType"] = options.RightInputType,
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

        if (side.HasValue)
        {
            attributes["side"] = side.Value.ToString();
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
        JoinRoutingOptions options,
        IFlowExpressionEngine expressionEngine,
        string? key = null,
        FlowJoinSide? side = null)
    {
        var values = new List<string>
        {
            $"leftInputType={options.LeftInputType}",
            $"rightInputType={options.RightInputType}",
            $"engine={expressionEngine.Name}",
            $"timeoutMilliseconds={options.TimeoutMilliseconds}",
            $"maxPending={options.MaxPending}"
        };

        if (!string.IsNullOrWhiteSpace(key))
        {
            values.Add($"key={key}");
        }

        if (side.HasValue)
        {
            values.Add($"side={side.Value}");
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
