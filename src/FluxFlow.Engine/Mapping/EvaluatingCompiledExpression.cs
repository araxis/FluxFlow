namespace FluxFlow.Engine.Mapping;

/// <summary>
/// Default <see cref="IFlowExpressionEngine.Compile{T}"/> result: defers to
/// <see cref="IFlowExpressionEngine.Evaluate{T}"/> on every call. Engines that
/// can pre-parse an expression should override <c>Compile</c> to return a
/// representation that parses once instead of using this wrapper.
/// </summary>
internal sealed class EvaluatingCompiledExpression<T> : IFlowCompiledExpression<T>
{
    private readonly IFlowExpressionEngine _engine;
    private readonly string _expression;

    public EvaluatingCompiledExpression(IFlowExpressionEngine engine, string expression)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        _expression = expression;
    }

    public T Evaluate(FlowMapContext context) => _engine.Evaluate<T>(_expression, context);
}
