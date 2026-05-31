namespace FluxFlow.Engine.Mapping;

public sealed class ExpressionFlowPredicate<TInput> : IFlowPredicate<TInput>
{
    private readonly string _expression;
    private readonly IFlowExpressionEngine _engine;
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
        _expression = expression;
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    }

    public bool IsMatch(TInput input)
        => _engine.Evaluate<bool>(_expression, _contextFactory.Create(input));

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
