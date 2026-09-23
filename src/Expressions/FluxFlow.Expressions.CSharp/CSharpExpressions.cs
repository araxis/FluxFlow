using System.Text.Json;
using System.Linq.Expressions;
using DynamicExpresso;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Mapping;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Expressions.CSharp;

public static class CSharpExpressions
{
    /// <summary>Registers C# by name; optionally selects it for links that omit Engine.</summary>
    public static IServiceCollection AddCSharpExpressionEngine(this IServiceCollection services, bool useAsDefault = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        var engine = new Engine();
        services.AddKeyedSingleton<IFlowExpressionEngine>("csharp", engine);
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
        public string Name => "csharp";

        public object? Evaluate(string expression, FlowMapContext context, Type resultType)
            => Compile(expression, resultType, context.Variables.ToDictionary(pair => pair.Key, pair => pair.Value?.GetType() ?? typeof(object)))(context);

        public Func<FlowMapContext, T> Compile<T>(string expression)
            => Compile<T>(expression, new Dictionary<string, Type>());

        public Func<FlowMapContext, T> Compile<T>(string expression, IReadOnlyDictionary<string, Type> parameterTypes)
        {
            var evaluate = Compile(expression, typeof(T), parameterTypes);
            return context => (T)evaluate(context)!;
        }

        private static Type ParameterType(Type type)
            => typeof(FlowValue).IsAssignableFrom(type) || type == typeof(JsonElement) ||
               typeof(IReadOnlyDictionary<string, FlowValue>).IsAssignableFrom(type) ? typeof(object) : type;

        private static object? ParameterValue(object? value) => value switch
        {
            FlowValue flow => ExpressionValue.Read(flow.Json),
            JsonElement json => ExpressionValue.Read(json),
            IReadOnlyDictionary<string, FlowValue> headers => new ExpressionValue(headers),
            _ => value
        };

        private Func<FlowMapContext, object?> Compile(string expression, Type resultType, IReadOnlyDictionary<string, Type> parameterTypes)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(expression);
            var types = new Dictionary<string, Type>(StringComparer.Ordinal)
            {
                ["value"] = typeof(object), ["headers"] = typeof(object),
                ["messageId"] = typeof(string), ["traceId"] = typeof(string),
                ["correlationId"] = typeof(string), ["causationId"] = typeof(string),
                ["timestamp"] = typeof(DateTimeOffset), ["input"] = typeof(object),
                ["payload"] = typeof(object), ["message"] = typeof(FlowMessage)
            };
            foreach (var (name, type) in parameterTypes) types[name] = ParameterType(type);
            var parameters = types.Select(pair => new Parameter(pair.Key, pair.Value)).ToArray();
            var interpreter = new Interpreter(InterpreterOptions.Default | InterpreterOptions.LateBindObject)
                .Reference(typeof(DateTimeOffset)).Reference(typeof(TimeSpan))
                .SetDefaultNumberType(DefaultNumberType.Decimal);
            var lambda = interpreter.Parse(expression,
                resultType == typeof(FlowValue) ? typeof(object) : resultType, parameters);
            var used = lambda.UsedParameters.ToArray();
            var arguments = Expression.Parameter(typeof(object[]), "arguments");
            var body = Expression.Invoke(Expression.Lambda(lambda.Expression, used.Select(parameter => parameter.Expression)),
                used.Select((parameter, index) => Expression.Convert(Expression.ArrayIndex(arguments, Expression.Constant(index)), parameter.Type)));
            var evaluate = Expression.Lambda<Func<object?[], object?>>(Expression.Convert(body, typeof(object)), arguments).Compile();
            return context =>
            {
                var result = evaluate(used.Select(parameter => ParameterValue(context.Variables[parameter.Name])).ToArray());
                return resultType == typeof(FlowValue)
                    ? new JsonFlowValue(JsonSerializer.SerializeToElement(result, ExpressionValue.SerializerOptions)) : result;
            };
        }
    }
}
