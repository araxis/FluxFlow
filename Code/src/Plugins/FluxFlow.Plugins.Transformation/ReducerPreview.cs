using System.Text.Json;
using FluxFlow.Components.Reducer;
using FluxFlow.Configuration.Expressions;
using FluxFlow.Core.Messaging;

namespace FluxFlow.Plugins.Transformation;

internal static class ReducerPreview
{
    public static Func<JsonElement, JsonElement> Compile(IServiceProvider services, bool equality,
        string expression, IReadOnlyDictionary<string, JsonElement> settings)
    {
        var name = settings.FirstOrDefault(item => item.Key.Equals("Engine", StringComparison.OrdinalIgnoreCase)).Value;
        if (name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString()))
            throw new ArgumentException("Select an expression engine.");
        var engine = services.GetExpressionEngine(name.GetString()!);
        if (equality)
        {
            var evaluate = ReducerExpressions.CompileEquality(engine, expression);
            return sample =>
            {
                var value = sample.GetProperty("Value");
                return JsonSerializer.SerializeToElement(evaluate(Read(value, "previous"), Read(value, "current")), FlowJsonContext.Default.Boolean);
            };
        }
        var reduce = ReducerExpressions.CompileReduce(engine, expression);
        return sample =>
        {
            var value = sample.GetProperty("Value");
            return reduce(Read(value, "state"), Read(value, "input")).Json.Clone();
        };
    }

    private static FlowValue Read(JsonElement sample, string property)
    {
        if (sample.ValueKind != JsonValueKind.Object || !sample.TryGetProperty(property, out var value))
            throw new ArgumentException($"Sample must contain '{property}'. Use null for an explicit JSON null value.");
        return new JsonFlowValue(value);
    }
}
