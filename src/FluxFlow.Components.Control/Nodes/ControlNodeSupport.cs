using FluxFlow.Components.Control.Options;

namespace FluxFlow.Components.Control.Nodes;

internal static class ControlNodeSupport
{
    public static Dictionary<string, object?> CreateAttributes(
        ControlExpressionOptions options,
        string engineName,
        bool? passed = null,
        string? route = null)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["inputType"] = options.InputType,
            ["engine"] = engineName
        };

        if (passed.HasValue)
        {
            attributes["passed"] = passed.Value;
        }

        if (!string.IsNullOrWhiteSpace(route))
        {
            attributes["route"] = route;
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
        ControlExpressionOptions options,
        string engineName)
    {
        var values = new List<string>
        {
            $"inputType={options.InputType}",
            $"engine={engineName}"
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
}
