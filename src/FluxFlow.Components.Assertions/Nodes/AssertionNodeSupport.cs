using FluxFlow.Components.Assertions.Contracts;
using FluxFlow.Components.Assertions.Options;
using FluxFlow.Engine.Mapping;

namespace FluxFlow.Components.Assertions.Nodes;

internal static class AssertionNodeSupport
{
    public static bool Evaluate(
        IFlowExpressionEngine expressionEngine,
        AssertionOptions options,
        IAssertionContextFactory contextFactory,
        AssertionNodeContext nodeContext,
        object? input)
    {
        var context = contextFactory.Create(input, nodeContext);
        var value = expressionEngine.Evaluate(options.Expression!, context, typeof(bool));
        return value switch
        {
            bool result => result,
            null => throw new InvalidOperationException(
                "Assertion expression returned null. Expected Boolean."),
            _ => throw new InvalidOperationException(
                $"Assertion expression returned '{value.GetType().Name}'. Expected Boolean.")
        };
    }

    public static Dictionary<string, object?> CreateAttributes(
        AssertionOptions options,
        IFlowExpressionEngine expressionEngine,
        bool? passed = null)
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
        AssertionOptions options,
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
