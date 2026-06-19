namespace FluxFlow.Mapping;

/// <summary>
/// A build-time mapper that compiles an expression once via
/// <see cref="IFlowExpressionEngine.Compile{T}"/> and evaluates the compiled
/// form per message. The mapped input is carried on the supplied
/// <see cref="FlowMapContext"/> by the context factory, matching
/// <see cref="ExpressionFlowPredicate{TInput}"/>.
/// </summary>
public sealed class ExpressionFlowMapper<TInput, TOutput> : IFlowMapper<TInput, TOutput>
{
    private readonly IFlowCompiledExpression<TOutput> _compiled;

    public ExpressionFlowMapper(string expression, IFlowExpressionEngine engine)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        ArgumentNullException.ThrowIfNull(engine);
        _compiled = engine.Compile<TOutput>(expression);
    }

    public TOutput Map(TInput input, FlowMapContext context)
        => _compiled.Evaluate(context);
}
