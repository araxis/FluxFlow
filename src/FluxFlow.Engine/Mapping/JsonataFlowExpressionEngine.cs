using Jsonata.Net.Native;
using System.Text.Json;

namespace FluxFlow.Engine.Mapping;

public sealed class JsonataFlowExpressionEngine : IFlowExpressionEngine
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public string Name => "jsonata";

    public object? Evaluate(string expression, FlowMapContext context, Type resultType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(resultType);

        var inputJson = JsonSerializer.Serialize(ToJsonataVariables(context.Variables), Options);
        var resultJson = new JsonataQuery(expression).Eval(inputJson);

        using var document = JsonDocument.Parse(resultJson);
        return ConvertElement(document.RootElement, resultType);
    }

    private static object? ConvertElement(JsonElement element, Type resultType)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (resultType == typeof(object))
        {
            return ConvertUntyped(element);
        }

        return element.Deserialize(resultType, Options);
    }

    private static object? ConvertUntyped(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.Clone()
        };

    private static IReadOnlyDictionary<string, object?> ToJsonataVariables(IReadOnlyDictionary<string, object?> variables)
        => variables
            .Where(pair => pair.Value is not Type and not Delegate)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
}
