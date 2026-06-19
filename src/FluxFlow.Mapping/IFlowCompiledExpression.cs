namespace FluxFlow.Mapping;

/// <summary>
/// An expression compiled once by an <see cref="IFlowExpressionEngine"/> for
/// repeated evaluation. Implementations must not re-parse the source expression
/// on each <see cref="Evaluate"/> call.
/// </summary>
public interface IFlowCompiledExpression<out T>
{
    T Evaluate(FlowMapContext context);
}
