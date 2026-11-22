using FluxFlow.Core.Mapping;

namespace FluxFlow.Testing;

// Contract test double: named callbacks, not an expression parser or language implementation.
internal sealed class TestExpressionEngine(params (string Name, Func<FlowMapContext, object?> Evaluate)[] rules)
    : IFlowExpressionEngine
{
    public string Name => "test";

    private Func<FlowMapContext, object?> Resolve(string expression)
        => rules.FirstOrDefault(rule => rule.Name == expression).Evaluate
            ?? (expression == "true" ? _ => true : expression == "false" ? _ => false
                : throw new ArgumentException($"No test callback registered for '{expression}'."));

    public object? Evaluate(string expression, FlowMapContext context, Type resultType)
        => Resolve(expression)(context);

    public Func<FlowMapContext, T> Compile<T>(string expression)
        => new Compiled<T>(Resolve(expression)).Evaluate;

    public Func<FlowMapContext, T> Compile<T>(string expression, IReadOnlyDictionary<string, Type> parameterTypes)
        => Compile<T>(expression);

    private sealed class Compiled<T>(Func<FlowMapContext, object?> evaluate)
    {
        public T Evaluate(FlowMapContext context) => (T)evaluate(context)!;
    }
}
