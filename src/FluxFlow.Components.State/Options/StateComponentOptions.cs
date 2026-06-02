using FluxFlow.Components.Expressions;
using FluxFlow.Engine.Mapping;

namespace FluxFlow.Components.State.Options;

public sealed class StateComponentOptions
{
    private readonly FlowExpressionEngineRegistry _expressionEngines = new("State");

    public StateComponentOptions UseExpressionEngine(
        IFlowExpressionEngine expressionEngine,
        bool useAsDefault = true)
    {
        ArgumentNullException.ThrowIfNull(expressionEngine);
        ArgumentException.ThrowIfNullOrWhiteSpace(expressionEngine.Name);

        _expressionEngines.Use(expressionEngine, useAsDefault);
        return this;
    }

    public StateComponentOptions UseExpressionEngineResolver(
        Func<string?, IFlowExpressionEngine> resolver)
    {
        _expressionEngines.UseResolver(resolver);
        return this;
    }

    internal IFlowExpressionEngine ResolveExpressionEngine(string? name)
        => _expressionEngines.Resolve(name);
}
