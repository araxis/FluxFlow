using FluxFlow.Components.Expressions;
using FluxFlow.Components.State.Timing;
using FluxFlow.Engine.Mapping;

namespace FluxFlow.Components.State.Options;

public sealed class StateComponentOptions
{
    private readonly FlowExpressionEngineRegistry _expressionEngines = new("State");
    private IStateClock _clock = SystemStateClock.Instance;

    public IStateClock Clock => _clock;

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

    public StateComponentOptions UseClock(IStateClock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        return this;
    }

    internal IFlowExpressionEngine ResolveExpressionEngine(string? name)
        => _expressionEngines.Resolve(name);
}
