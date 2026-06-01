using FluxFlow.Engine.Mapping;

namespace FluxFlow.Components.State.Options;

public sealed class StateComponentOptions
{
    private readonly Dictionary<string, IFlowExpressionEngine> _expressionEngines =
        new(StringComparer.OrdinalIgnoreCase);
    private IFlowExpressionEngine? _defaultExpressionEngine;
    private Func<string?, IFlowExpressionEngine>? _expressionEngineResolver;

    public StateComponentOptions UseExpressionEngine(
        IFlowExpressionEngine expressionEngine,
        bool useAsDefault = true)
    {
        ArgumentNullException.ThrowIfNull(expressionEngine);
        ArgumentException.ThrowIfNullOrWhiteSpace(expressionEngine.Name);

        _expressionEngines[expressionEngine.Name] = expressionEngine;
        if (useAsDefault || _defaultExpressionEngine is null)
        {
            _defaultExpressionEngine = expressionEngine;
        }

        return this;
    }

    public StateComponentOptions UseExpressionEngineResolver(
        Func<string?, IFlowExpressionEngine> resolver)
    {
        _expressionEngineResolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        return this;
    }

    internal IFlowExpressionEngine ResolveExpressionEngine(string? name)
    {
        if (_expressionEngineResolver is not null)
        {
            var resolved = _expressionEngineResolver(name);
            return resolved ?? throw new InvalidOperationException(
                "State expression engine resolver returned null.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return _defaultExpressionEngine ?? throw new InvalidOperationException(
                "State components require an expression engine.");
        }

        if (_expressionEngines.TryGetValue(name.Trim(), out var expressionEngine))
        {
            return expressionEngine;
        }

        throw new InvalidOperationException(
            $"State expression engine '{name}' is not registered.");
    }
}
