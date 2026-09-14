using System.Text.Json;
using FluxFlow.Core.Addressing;
using FluxFlow.Json.Model;
using FluxFlow.Mapping;
using FluxFlow.Nodes;

namespace FluxFlow.Json.Links;

// One compilation session owns resolution and caches; no process-global state.
internal sealed class ApplicationConditionCompiler(
    ApplicationDefinition definition,
    IFlowExpressionEngine? defaultEngine,
    Func<string, IFlowExpressionEngine?>? resolveEngine)
{
    private readonly Dictionary<string, IFlowExpressionEngine?> _engines = new(StringComparer.Ordinal);
    private readonly Dictionary<CacheKey, IFlowCompiledExpression<bool>> _compiled = [];
    private readonly Dictionary<CacheKey, Exception> _failures = [];

    internal IFlowCompiledExpression<bool> Compile(ApplicationAddress source, string expression,
        Type inputType, IReadOnlyDictionary<string, JsonElement>? parameters)
    {
        var name = definition.ExpressionEngine;
        if (source.Kind == ApplicationAddressKind.WorkflowPort &&
            definition.Workflows.TryGetValue(source.Segments[0], out var workflow))
            name = workflow.ExpressionEngine ?? name;

        var engine = defaultEngine;
        if (name is not null && !_engines.TryGetValue(name, out engine))
        {
            engine = resolveEngine?.Invoke(name);
            _engines.Add(name, engine);
        }
        if (engine is null)
            throw new MissingConditionEngineException(name);

        var values = (parameters ?? ConditionParameters.Copy(null))
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(static pair => pair.Key, static pair => ConditionParameters.Value(pair.Value), StringComparer.Ordinal);
        var signature = JsonSerializer.Serialize(values);
        var key = new CacheKey(name, expression, inputType, signature);
        if (_compiled.TryGetValue(key, out var compiled))
            return compiled;
        if (_failures.TryGetValue(key, out var failure))
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();

        var types = values.ToDictionary(static pair => pair.Key, static pair => pair.Value?.GetType() ?? typeof(object), StringComparer.Ordinal);
        types.Add("input", inputType);
        types.Add("payload", inputType);
        types.Add("message", inputType == typeof(global::FluxFlow.Data.FlowValue)
            ? typeof(FlowMessage)
            : typeof(FlowMessage<>).MakeGenericType(inputType));
        IFlowCompiledExpression<bool> expressionBody;
        try
        {
            expressionBody = engine.Compile<bool>(expression, types)
                ?? throw new InvalidOperationException($"Expression engine '{engine.Name}' returned no compiled condition.");
        }
        catch (Exception exception)
        {
            _failures.Add(key, exception);
            throw;
        }
        compiled = values.Count == 0 ? expressionBody : new BoundCondition(expressionBody, values);
        _compiled.Add(key, compiled);
        return compiled;
    }

    private sealed record CacheKey(string? Engine, string Expression, Type InputType, string Parameters);

    private sealed class BoundCondition(IFlowCompiledExpression<bool> expression, IReadOnlyDictionary<string, object?> parameters)
        : IFlowCompiledExpression<bool>
    {
        public bool Evaluate(FlowMapContext context)
        {
            var values = new Dictionary<string, object?>(context.Variables, StringComparer.Ordinal);
            foreach (var (name, value) in parameters)
                values[name] = value;
            return expression.Evaluate(new FlowMapContext { Variables = values });
        }
    }
}

internal sealed class MissingConditionEngineException(string? name)
    : InvalidOperationException(name is null
        ? "Conditional links require an expression engine."
        : $"Expression engine '{name}' is not registered.")
{
    internal string? EngineName { get; } = name;
}
