using FluxFlow.Engine.Mapping;

namespace FluxFlow.Components.State.Nodes;

/// <summary>
/// <see cref="IFlowReducer"/> backed by expressions compiled once via
/// <see cref="IFlowExpressionEngine.Compile{T}"/>. The reducer expression is
/// always present; the key selector is optional.
/// </summary>
internal sealed class CompiledFlowReducer : IFlowReducer
{
    private readonly IFlowCompiledExpression<object?> _reducer;
    private readonly IFlowCompiledExpression<string?>? _key;

    public CompiledFlowReducer(
        IFlowCompiledExpression<object?> reducer,
        IFlowCompiledExpression<string?>? key)
    {
        _reducer = reducer ?? throw new ArgumentNullException(nameof(reducer));
        _key = key;
    }

    public object? Reduce(FlowMapContext context) => _reducer.Evaluate(context);

    public string? ResolveKey(FlowMapContext context)
        => _key is null ? null : _key.Evaluate(context);
}
