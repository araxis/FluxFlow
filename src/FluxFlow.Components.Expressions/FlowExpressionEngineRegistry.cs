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
        ArgumentException.ThrowIfNullOrWhiteSpace(expressionEngine.Name);

        _expressionEngines[expressionEngine.Name.Trim()] = expressionEngine;
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
        if (_resolver is not null)
        {
            var resolved = _resolver(name);
            return resolved ?? throw new InvalidOperationException(
                $"{_scopeName} expression engine resolver returned null.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return _defaultExpressionEngine ?? throw new InvalidOperationException(
                $"{_scopeName} components require an expression engine.");
        }

        if (_expressionEngines.TryGetValue(name.Trim(), out var expressionEngine))
        {
            return expressionEngine;
        }

        throw new InvalidOperationException(
            $"{_scopeName} expression engine '{name}' is not registered.");
    }
}
