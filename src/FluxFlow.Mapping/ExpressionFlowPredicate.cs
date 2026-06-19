namespace FluxFlow.Mapping;

public sealed class ExpressionFlowPredicate<TInput> : IFlowPredicate<TInput>
{
    private readonly IFlowCompiledExpression<bool> _compiled;
    private readonly IFlowMapContextFactory<TInput> _contextFactory;

    public ExpressionFlowPredicate(
        string expression,
        IFlowExpressionEngine engine)
        : this(expression, engine, new DefaultFlowMapContextFactory())
    {
    }

    public ExpressionFlowPredicate(
        string expression,
        IFlowExpressionEngine engine,
        IFlowMapContextFactory<TInput> contextFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        ArgumentNullException.ThrowIfNull(engine);
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        // Compile once here (build time); IsMatch only evaluates the compiled form.
        _compiled = engine.Compile<bool>(expression);
    }

    public bool IsMatch(TInput input)
        => _compiled.Evaluate(_contextFactory.Create(input));

    private sealed class DefaultFlowMapContextFactory : IFlowMapContextFactory<TInput>
    {
        public FlowMapContext Create(TInput input)
            => new()
            {
                Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["input"] = input,
                    ["value"] = input
                }
            };
    }
}
