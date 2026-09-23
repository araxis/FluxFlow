using System.Text.Json;
using FluxFlow.Core.Mapping;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Configuration.Links;

// One compilation session owns resolution and caches; no process-global state.
internal sealed class ApplicationConditionCompiler(
    IFlowExpressionEngine? defaultEngine,
    Func<string, IFlowExpressionEngine?>? resolveEngine)
{
    private readonly Dictionary<string, IFlowExpressionEngine?> _engines = new(StringComparer.Ordinal);
    private readonly Dictionary<CacheKey, Lazy<Func<FlowMapContext, bool>>> _compiled = [];

    internal Func<FlowMapContext, bool> Compile(string? name, string expression,
        Type inputType, IReadOnlyDictionary<string, JsonElement>? parameters)
    {
        var engine = defaultEngine;
        if (name is not null && !_engines.TryGetValue(name, out engine))
        {
            engine = resolveEngine?.Invoke(name);
            _engines.Add(name, engine);
        }
        if (engine is null)
            throw new InvalidOperationException(name is null
                ? "Conditional links require an expression engine."
                : $"Expression engine '{name}' is not registered.");

        var values = (parameters ?? ConditionParameters.Copy(null))
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(static pair => pair.Key, static pair => ConditionParameters.Value(pair.Value), StringComparer.Ordinal);
        var signature = JsonSerializer.Serialize(values);
        var key = new CacheKey(name, expression, inputType, signature);
        if (!_compiled.TryGetValue(key, out var compiled))
        {
            // Lazy retains either the expression or its original compilation failure
            // for this session. Message evaluation itself is never cached.
            compiled = new Lazy<Func<FlowMapContext, bool>>(() =>
            {
                var types = values.ToDictionary(static pair => pair.Key, static pair => pair.Value?.GetType() ?? typeof(object), StringComparer.Ordinal);
                types.Add("input", inputType);
                types.Add("payload", inputType);
                types.Add("message", typeof(FlowMessage));
                var expressionBody = engine.Compile<bool>(expression, types)
                    ?? throw new InvalidOperationException($"Expression engine '{engine.Name}' returned no compiled condition.");
                if (values.Count == 0) return expressionBody;
                return context =>
                {
                    var bound = new Dictionary<string, object?>(context.Variables, StringComparer.Ordinal);
                    foreach (var (name, value) in values) bound[name] = value;
                    return expressionBody(new FlowMapContext { Variables = bound });
                };
            });
            _compiled.Add(key, compiled);
        }
        return compiled.Value;
    }

    private sealed record CacheKey(string? Engine, string Expression, Type InputType, string Parameters);

}
