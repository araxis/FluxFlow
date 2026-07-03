using FluxFlow.Mapping;

namespace FluxFlow.Components.Expressions;

public sealed class FlowExpressionEngineRegistry
{
    private readonly string _scopeName;
    private readonly Dictionary<string, IFlowExpressionEngine> _expressionEngines =
        new(StringComparer.OrdinalIgnoreCase);
    private IFlowExpressionEngine? _defaultExpressionEngine;
    private Func<string?, IFlowExpressionEngine>? _resolver;

    public FlowExpressionEngineRegistry(string scopeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeName);
        _scopeName = scopeName.Trim();
    }

    public FlowExpressionEngineRegistry Use(
        IFlowExpressionEngine expressionEngine,
        bool useAsDefault = true)
    {
        ArgumentNullException.ThrowIfNull(expressionEngine);
        var engineName = NormalizeName(expressionEngine.Name);
        if (engineName is null)
        {
            throw new ArgumentException(
                "Expression engine name is required.",
                nameof(expressionEngine));
        }

        _expressionEngines[engineName] = expressionEngine;
        if (useAsDefault)
        {
            _defaultExpressionEngine = expressionEngine;
        }

        return this;
    }

    public FlowExpressionEngineRegistry UseResolver(
        Func<string?, IFlowExpressionEngine> resolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        return this;
    }

    public IFlowExpressionEngine Resolve(string? name)
    {
        var engineName = NormalizeName(name);

        if (_resolver is not null)
        {
            var resolved = _resolver(engineName);
            return resolved ?? throw new InvalidOperationException(
                $"{_scopeName} expression engine resolver returned null.");
        }

        if (engineName is null)
        {
            return _defaultExpressionEngine ?? throw new InvalidOperationException(
                $"{_scopeName} components require an expression engine.");
        }

        if (_expressionEngines.TryGetValue(engineName, out var expressionEngine))
        {
            return expressionEngine;
        }

        throw new InvalidOperationException(
            $"{_scopeName} expression engine '{engineName}' is not registered.");
    }

    private static string? NormalizeName(string? name)
        => string.IsNullOrWhiteSpace(name) ? null : name.Trim();
}
