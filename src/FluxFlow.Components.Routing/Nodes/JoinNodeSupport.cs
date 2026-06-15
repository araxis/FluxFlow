using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Options;

namespace FluxFlow.Components.Routing.Nodes;

internal static class JoinNodeSupport
{
    public static Dictionary<string, object?> CreateAttributes(
        JoinRoutingOptions options,
        string? engineName,
        int pendingCount,
        string? key = null,
        FlowJoinSide? side = null)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["leftInputType"] = options.LeftInputType,
            ["rightInputType"] = options.RightInputType,
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
        string? engineName,
        string? key = null,
        FlowJoinSide? side = null)
    {
        var values = new List<string>
        {
            $"leftInputType={options.LeftInputType}",
            $"rightInputType={options.RightInputType}",
            $"engine={engineName}",
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
}
