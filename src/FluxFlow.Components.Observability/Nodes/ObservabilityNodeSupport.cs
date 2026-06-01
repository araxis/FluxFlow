using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Components.Observability.Options;
using FluxFlow.Engine.Mapping;
using System.Collections;
using System.Globalization;

namespace FluxFlow.Components.Observability.Nodes;

internal static class ObservabilityNodeSupport
{
    public static bool EvaluatePredicate(
        IFlowExpressionEngine expressionEngine,
        IObservabilityContextFactory contextFactory,
        ObservabilityNodeContext nodeContext,
        string predicate,
        object? input)
    {
        var context = contextFactory.Create(input, nodeContext);
        var value = expressionEngine.Evaluate(predicate, context, typeof(bool));
        return value switch
        {
            bool result => result,
            null => throw new InvalidOperationException(
                "Observability predicate returned null. Expected Boolean."),
            _ => throw new InvalidOperationException(
                $"Observability predicate returned '{value.GetType().Name}'. Expected Boolean.")
        };
    }

    public static double? ConvertSize(object? value)
    {
        if (value is null)
        {
            return null;
        }

        return value switch
        {
            byte[] bytes => bytes.LongLength,
            string text => text.Length,
            sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal
                => Convert.ToDouble(value, CultureInfo.InvariantCulture),
            ICollection collection => collection.Count,
            _ => throw new InvalidOperationException(
                $"Size selector returned '{value.GetType().Name}'. Expected a numeric value, string, bytes, or collection.")
        };
    }

    public static string RenderMessage(
        string template,
        IReadOnlyDictionary<string, object?> values)
    {
        var rendered = template;
        foreach (var (key, value) in values)
        {
            rendered = rendered.Replace(
                "{" + key + "}",
                Convert.ToString(value, CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        }

        return rendered;
    }

    public static Dictionary<string, object?> CreateAttributes(
        string nodeType,
        string inputType,
        string? name = null,
        long? count = null)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["nodeType"] = nodeType,
            ["inputType"] = inputType
        };

        if (!string.IsNullOrWhiteSpace(name))
        {
            attributes["name"] = name;
        }

        if (count.HasValue)
        {
            attributes["count"] = count.Value;
        }

        return attributes;
    }

    public static string CreateExpressionContext(
        FlowCounterOptions options,
        string? engineName)
    {
        var values = new List<string>
        {
            $"inputType={options.InputType}"
        };

        if (!string.IsNullOrWhiteSpace(options.Name))
        {
            values.Add($"name={options.Name}");
        }

        if (!string.IsNullOrWhiteSpace(engineName))
        {
            values.Add($"engine={engineName}");
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
