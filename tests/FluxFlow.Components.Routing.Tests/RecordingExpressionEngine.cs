using FluxFlow.Mapping;

namespace FluxFlow.Components.Routing.Tests;

internal sealed class RecordingExpressionEngine(
    string name = "test",
    Func<string, FlowMapContext, Type, object?>? evaluate = null)
    : IFlowExpressionEngine
{
    public string Name => name;

    public object? Evaluate(string expression, FlowMapContext context, Type resultType)
        => evaluate?.Invoke(expression, context, resultType) ?? context.Variables["input"];
}
