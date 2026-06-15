using FluxFlow.Components.Observability.Options;
using System.Collections;
using System.Globalization;
using System.Text;

namespace FluxFlow.Components.Observability.Nodes;

internal static class ObservabilityNodeSupport
{
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
        var rendered = new StringBuilder(template.Length);
        var position = 0;
        while (position < template.Length)
        {
            var start = template.IndexOf('{', position);
            var end = start < 0 ? -1 : template.IndexOf('}', start + 1);
            if (end < 0)
            {
                rendered.Append(template, position, template.Length - position);
                break;
            }

            var key = template.Substring(start + 1, end - start - 1);
            if (values.TryGetValue(key, out var value))
            {
                rendered.Append(template, position, start - position);
                rendered.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                position = end + 1;
            }
            else
            {
                rendered.Append(template, position, start + 1 - position);
                position = start + 1;
            }
        }

        return rendered.ToString();
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
