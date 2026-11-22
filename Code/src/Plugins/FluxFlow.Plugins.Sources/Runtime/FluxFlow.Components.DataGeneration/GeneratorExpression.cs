using System.Globalization;
using System.Text.Json;
using FluxFlow.Core.Mapping;
using FluxFlow.Core.Messaging;
using Jsonata.Net.Native;
using Jsonata.Net.Native.Json;
using Jsonata.Net.Native.SystemTextJson;
using JsonataExtensions = Jsonata.Net.Native.SystemTextJson.JsonataExtensions;

namespace FluxFlow.Components.DataGeneration;

internal sealed class GeneratorExpression
{
    private readonly JsonataQuery _query;
    private readonly BogusFunctions _functions;

    internal GeneratorExpression(string expression, string locale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        _query = new JsonataQuery(expression);
        _functions = new BogusFunctions(locale);
    }

    internal FlowValue Evaluate(FlowMessage message)
    {
        var environment = new EvaluationEnvironment();
        _functions.Bind(environment);
        var input = JsonataExtensions.FromSystemTextJson(message.Value.Json);
        foreach (var (name, value) in FlowMapContext.ForMessage(message).Variables)
            environment.BindValue(name, ReferenceEquals(value, message.Value) ? input : Token(value));
        var result = _query.Eval(input, environment);
        if (result.Type == JTokenType.Undefined)
            throw new InvalidOperationException("The generator expression returned undefined; return null explicitly for a JSON null value.");
        using var document = result.ToSystemTextJson();
        return new JsonFlowValue(document.RootElement);
    }

    private static JToken Token(object? value) => value switch
    {
        null => JValue.CreateNull(),
        FlowValue flow => JsonataExtensions.FromSystemTextJson(flow.Json),
        DateTimeOffset timestamp => new JValue(timestamp.ToString("O", CultureInfo.InvariantCulture)),
        IReadOnlyDictionary<string, FlowValue> headers => Headers(headers),
        _ => JsonataExtensions.FromSystemTextJson(JsonSerializer.SerializeToElement(value))
    };

    private static JObject Headers(IReadOnlyDictionary<string, FlowValue> headers)
    {
        var result = new JObject();
        foreach (var (name, value) in headers) result.Add(name, Token(value));
        return result;
    }
}
