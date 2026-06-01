using FluxFlow.Components.Control.Contracts;
using FluxFlow.Components.Control.Options;
using FluxFlow.Engine.Mapping;

namespace FluxFlow.Components.Control.Nodes;

internal static class ControlNodeSupport
{
    public static bool Evaluate(
        IFlowExpressionEngine expressionEngine,
        ControlExpressionOptions options,
        IControlContextFactory contextFactory,
        ControlNodeContext nodeContext,
        object? input)
    {
        var context = contextFactory.Create(input, nodeContext);
        var value = expressionEngine.Evaluate(options.Expression!, context, typeof(bool));
        return value switch
        {
            bool result => result,
            null => throw new InvalidOperationException(
                "Control expression returned null. Expected Boolean."),
            _ => throw new InvalidOperationException(
                $"Control expression returned '{value.GetType().Name}'. Expected Boolean.")
        };
    }

    public static Dictionary<string, object?> CreateAttributes(
        ControlExpressionOptions options,
        IFlowExpressionEngine expressionEngine,
        bool? passed = null,
        string? route = null)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["inputType"] = options.InputType,
            ["engine"] = expressionEngine.Name
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
        IFlowExpressionEngine expressionEngine)
    {
        var values = new List<string>
        {
            $"inputType={options.InputType}",
            $"engine={expressionEngine.Name}"
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
