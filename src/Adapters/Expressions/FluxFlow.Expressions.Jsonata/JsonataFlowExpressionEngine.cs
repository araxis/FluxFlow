using System.Text.Json;
using FluxFlow.Mapping;
using Jsonata.Net.Native;

namespace FluxFlow.Expressions.Jsonata;

public sealed class JsonataFlowExpressionEngine : IFlowExpressionEngine
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    public string Name => "jsonata";

    public object? Evaluate(
        string expression,
        FlowMapContext context,
        Type resultType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(resultType);

        return Evaluate(
            new JsonataQuery(expression),
            context,
            resultType);
    }

    public IFlowCompiledExpression<T> Compile<T>(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        return new CompiledExpression<T>(new JsonataQuery(expression));
    }

    private static object? Evaluate(
        JsonataQuery query,
        FlowMapContext context,
        Type resultType)
    {
        var inputJson = JsonSerializer.Serialize(
            ToJsonataVariables(context.Variables),
            SerializerOptions);
        var resultJson = query.Eval(inputJson);

        using var document = JsonDocument.Parse(resultJson);
        return ConvertElement(document.RootElement, resultType);
    }

    private static object? ConvertElement(JsonElement element, Type resultType)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (resultType == typeof(JsonElement))
        {
            return element.Clone();
        }

        if (resultType == typeof(object))
        {
            return ConvertUntyped(element);
        }

        return element.Deserialize(resultType, SerializerOptions);
    }

    private static object? ConvertUntyped(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.Clone(),
        };

    private static IReadOnlyDictionary<string, object?> ToJsonataVariables(
        IReadOnlyDictionary<string, object?> variables) =>
        variables
            .Where(static pair => pair.Value is not Type and not Delegate)
            .ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.Ordinal);

    private sealed class CompiledExpression<T>(JsonataQuery query)
        : IFlowCompiledExpression<T>
    {
        public T Evaluate(FlowMapContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return (T)JsonataFlowExpressionEngine.Evaluate(
                query,
                context,
                typeof(T))!;
        }
    }
}
