using FluxFlow.Components.Assertions.Contracts;

namespace FluxFlow.Components.Assertions.Nodes;

internal static class AssertionNodeSupport
{
    public static Dictionary<string, object?> CreateAttributes(
        AssertionResultMetadata metadata,
        bool? passed = null)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["inputType"] = metadata.InputType,
            ["engine"] = metadata.EngineName
        };

        if (passed.HasValue)
        {
            attributes["passed"] = passed.Value;
        }

        if (!string.IsNullOrWhiteSpace(metadata.ExpressionId))
        {
            attributes["expressionId"] = metadata.ExpressionId;
        }

        if (!string.IsNullOrWhiteSpace(metadata.ExpressionName))
        {
            attributes["expressionName"] = metadata.ExpressionName;
        }

        return attributes;
    }

    public static string CreateErrorContext(AssertionResultMetadata metadata)
    {
        var values = new List<string>
        {
            $"inputType={metadata.InputType}",
            $"engine={metadata.EngineName}"
        };

        if (!string.IsNullOrWhiteSpace(metadata.ExpressionId))
        {
            values.Add($"expressionId={metadata.ExpressionId}");
        }

        if (!string.IsNullOrWhiteSpace(metadata.ExpressionName))
        {
            values.Add($"expressionName={metadata.ExpressionName}");
        }

        return string.Join("; ", values);
    }
}
