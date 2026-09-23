using System.Globalization;
using System.Text.Json;
using Jsonata.Net.Native;
using Jsonata.Net.Native.Json;
using Jsonata.Net.Native.SystemTextJson;
using JsonataExtensions = Jsonata.Net.Native.SystemTextJson.JsonataExtensions;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Mapping;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Expressions.Jsonata;

public static class JsonataExpressions
{
    /// <summary>Registers JSONata by name; optionally selects it for links that omit Engine.</summary>
    public static IServiceCollection AddJsonataExpressionEngine(this IServiceCollection services, bool useAsDefault = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        var engine = new Engine();
        services.AddKeyedSingleton<IFlowExpressionEngine>("jsonata", engine);
        if (useAsDefault) services.AddSingleton<IFlowExpressionEngine>(engine);
        return services;
    }

    public static Func<FlowMessage, CancellationToken, ValueTask<FlowValue>> Compile(string expression)
    {
        var evaluate = new Engine().Compile<FlowValue>(expression);
        return (message, cancellation) =>
        {
            cancellation.ThrowIfCancellationRequested();
            var result = evaluate(FlowMapContext.ForMessage(message));
            cancellation.ThrowIfCancellationRequested();
            return ValueTask.FromResult(result);
        };
    }

    private sealed class Engine : IFlowExpressionEngine
    {
        public string Name => "jsonata";
        public object? Evaluate(string expression, FlowMapContext context, Type resultType)
            => Compile(expression, resultType)(context);
        public Func<FlowMapContext, T> Compile<T>(string expression)
        {
            var evaluate = Compile(expression, typeof(T));
            return context => (T)evaluate(context)!;
        }
        public Func<FlowMapContext, T> Compile<T>(string expression, IReadOnlyDictionary<string, Type> parameterTypes)
            => Compile<T>(expression);

        private static JToken Token(object? value) => value switch
        {
            null => JValue.CreateNull(),
            FlowValue flow => JsonataExtensions.FromSystemTextJson(flow.Json),
            JsonElement json => JsonataExtensions.FromSystemTextJson(json),
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

        private static Func<FlowMapContext, object?> Compile(string expression, Type resultType)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(expression);
            var query = new JsonataQuery(expression);
            return context =>
            {
                var environment = new EvaluationEnvironment();
                context.Variables.TryGetValue("input", out var input);
                var inputToken = Token(input);
                foreach (var (name, value) in context.Variables)
                    environment.BindValue(name, ReferenceEquals(input, value) ? inputToken : Token(value));
                var result = query.Eval(inputToken, environment);
                if (result.Type == JTokenType.Undefined)
                    throw new InvalidOperationException("JSONata expression returned undefined; return null explicitly for a JSON null value.");
                using var document = result.ToSystemTextJson();
                return resultType == typeof(FlowValue) ? new JsonFlowValue(document.RootElement)
                    : document.RootElement.Deserialize(resultType);
            };
        }
    }
}
