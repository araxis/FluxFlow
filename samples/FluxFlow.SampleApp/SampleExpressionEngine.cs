using FluxFlow.Engine.Mapping;

namespace FluxFlow.SampleApp;

internal sealed class SampleExpressionEngine : IFlowExpressionEngine
{
    public string Name => "sample";

    public object? Evaluate(
        string expression,
        FlowMapContext context,
        Type resultType)
    {
        if (resultType != typeof(bool))
        {
            throw new InvalidOperationException(
                $"Sample expression result type '{resultType.Name}' is not supported.");
        }

        var input = context.Variables["input"];
        if (input is not ReviewedOrder order)
        {
            throw new InvalidOperationException(
                $"Sample expression input type '{input?.GetType().Name ?? "null"}' is not supported.");
        }

        return expression switch
        {
            "input.Priority == true" => order.Priority,
            "input.Priority == false" => !order.Priority,
            _ => throw new InvalidOperationException(
                $"Sample expression '{expression}' is not supported.")
        };
    }
}
