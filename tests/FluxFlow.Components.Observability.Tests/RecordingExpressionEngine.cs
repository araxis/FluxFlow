using FluxFlow.Engine.Mapping;

namespace FluxFlow.Components.Observability.Tests;

internal sealed class RecordingExpressionEngine(
    string name,
    Func<string, FlowMapContext, Type, object?> evaluate)
    : IFlowExpressionEngine
{
    public RecordingExpressionEngine(Func<string, FlowMapContext, Type, object?> evaluate)
        : this("default", evaluate)
    {
    }

    public string Name { get; } = name;

    public object? Evaluate(string expression, FlowMapContext context, Type resultType)
        => evaluate(expression, context, resultType);
}
