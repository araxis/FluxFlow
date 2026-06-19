using FluxFlow.Mapping;

namespace FluxFlow.MappingControlSample;

internal sealed class SampleExpressionEngine : IFlowExpressionEngine
{
    public string Name => "sample";

    public object? Evaluate(string expression, FlowMapContext context, Type resultType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(resultType);

        return expression.Trim() switch
        {
            "review-order" => ReviewOrder(GetInput<IncomingOrder>(context), resultType),
            "order-is-active" => GetInput<ReviewedOrder>(context).Active,
            "order-is-priority" => GetInput<ReviewedOrder>(context).Priority,
            "order-total-valid" => GetInput<ReviewedOrder>(context).Total >= 0m,
            _ => throw new InvalidOperationException($"Sample expression '{expression}' is not supported.")
        };
    }

    private static ReviewedOrder ReviewOrder(IncomingOrder input, Type resultType)
    {
        if (resultType != typeof(ReviewedOrder))
        {
            throw new InvalidOperationException(
                $"review-order expected result type '{nameof(ReviewedOrder)}'.");
        }

        return new ReviewedOrder(
            input.Id,
            input.Customer,
            input.Total,
            input.Active,
            Priority: input.Total >= 100m);
    }

    private static TInput GetInput<TInput>(FlowMapContext context)
        => context.Variables.TryGetValue("input", out var value) && value is TInput input
            ? input
            : throw new InvalidOperationException(
                $"Sample expression expected input type '{typeof(TInput).Name}'.");
}

internal sealed class IncomingOrderContextFactory : IFlowMapContextFactory<IncomingOrder>
{
    public FlowMapContext Create(IncomingOrder input)
        => new()
        {
            Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["input"] = input,
                ["value"] = input,
                ["orderId"] = input.Id,
                ["customer"] = input.Customer,
                ["total"] = input.Total,
                ["active"] = input.Active
            }
        };
}

internal sealed class ReviewedOrderContextFactory : IFlowMapContextFactory<ReviewedOrder>
{
    public FlowMapContext Create(ReviewedOrder input)
        => new()
        {
            Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["input"] = input,
                ["value"] = input,
                ["orderId"] = input.Id,
                ["customer"] = input.Customer,
                ["total"] = input.Total,
                ["active"] = input.Active,
                ["priority"] = input.Priority
            }
        };
}
