namespace FluxFlow.Engine.Mapping;

public interface IFlowExpressionEngine
{
    string Name { get; }

    object? Evaluate(string expression, FlowMapContext context, Type resultType);

    T Evaluate<T>(string expression, FlowMapContext context)
        => (T)Evaluate(expression, context, typeof(T))!;
}
