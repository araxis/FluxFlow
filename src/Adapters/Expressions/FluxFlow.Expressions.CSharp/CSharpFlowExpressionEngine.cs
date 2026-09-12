using DynamicExpresso;
using FluxFlow.Mapping;

namespace FluxFlow.Expressions.CSharp;

/// <summary>Host-configured expressions with explicit types and invocation-local values.</summary>
public sealed class CSharpFlowExpressionEngine : IFlowExpressionEngine
{
    private readonly Interpreter _interpreter;
    private readonly IReadOnlyDictionary<string, Type> _parameterTypes;

    /// <remarks>
    /// Configuration runs once. Do not retain or mutate the interpreter after this callback.
    /// Expose only trusted types/functions; this interpreter is not a security sandbox.
    /// Referenced objects/functions must be safe for concurrent evaluation.
    /// </remarks>
    public CSharpFlowExpressionEngine(Action<Interpreter>? configure = null,
        InterpreterOptions options = InterpreterOptions.Default,
        IReadOnlyDictionary<string, Type>? parameterTypes = null)
    {
        _interpreter = new Interpreter(options).EnableAssignment(AssignmentOperators.None);
        configure?.Invoke(_interpreter);
        _parameterTypes = new Dictionary<string, Type>(parameterTypes ?? new Dictionary<string, Type>(), StringComparer.Ordinal);
    }

    public string Name => "csharp";

    public object? Evaluate(string expression, FlowMapContext context, Type resultType)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(resultType);
        var types = context.Variables.ToDictionary(static pair => pair.Key,
            static pair => pair.Value?.GetType() ?? typeof(object), StringComparer.Ordinal);
        var body = Parse(expression, resultType, types);
        return Invoke(body, context);
    }

    public IFlowCompiledExpression<T> Compile<T>(string expression)
        => Compile<T>(expression, _parameterTypes);

    public IFlowCompiledExpression<T> Compile<T>(string expression, IReadOnlyDictionary<string, Type> parameterTypes)
    {
        ArgumentNullException.ThrowIfNull(parameterTypes);
        return new Compiled<T>(Parse(expression, typeof(T), parameterTypes));
    }

    private Lambda Parse(string expression, Type resultType,
        IReadOnlyDictionary<string, Type> parameterTypes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        var types = new Dictionary<string, Type>(_parameterTypes, StringComparer.Ordinal);
        foreach (var (name, type) in parameterTypes)
            types[name] = type;
        var parameters = types.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => new Parameter(pair.Key, pair.Value)).ToArray();
        return _interpreter.Parse(expression, resultType, parameters);
    }

    private static object? Invoke(Lambda body, FlowMapContext context)
        => body.Invoke(body.UsedParameters.Select(parameter =>
            new Parameter(parameter.Name, parameter.Type, context.Variables[parameter.Name])));

    private sealed class Compiled<T>(Lambda body) : IFlowCompiledExpression<T>
    {
        public T Evaluate(FlowMapContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return (T)Invoke(body, context)!;
        }
    }
}
