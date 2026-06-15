using FluxFlow.Components.Routing.Options;

namespace FluxFlow.Components.Routing.Nodes;

internal static class CorrelationNodeSupport
{
    public static Dictionary<string, object?> CreateAttributes(
        CorrelationRoutingOptions options,
        string? engineName,
        int pendingCount,
        string? key = null,
        string? side = null)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["inputType"] = options.InputType,
            ["engine"] = engineName,
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
        string? engineName,
        string? key = null,
        string? side = null)
    {
        var values = new List<string>
        {
            $"inputType={options.InputType}",
            $"engine={engineName}",
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
}
