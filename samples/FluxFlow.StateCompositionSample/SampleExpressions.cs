using FluxFlow.Components.State.Contracts;
using FluxFlow.Components.Timers.Contracts;
using FluxFlow.Engine.Mapping;
using System.Text.Json;

namespace FluxFlow.StateCompositionSample;

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
            "tick-to-state-input" => CreateStateInput(GetInput<TimerTick>(context), resultType),
            "count-ticks" => ReadNumber(context.Variables["state"]) + 1,
            _ => throw new InvalidOperationException($"Sample expression '{expression}' is not supported.")
        };
    }

    private static StateReducerInput CreateStateInput(TimerTick tick, Type resultType)
    {
        if (resultType != typeof(StateReducerInput))
        {
            throw new InvalidOperationException(
                $"tick-to-state-input expected result type '{nameof(StateReducerInput)}'.");
        }

        return new StateReducerInput
        {
            Key = "ticks",
            Input = tick.Sequence,
            Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["tickName"] = tick.Name,
                ["tickTimestamp"] = tick.Timestamp,
                ["tickDueAt"] = tick.DueAt
            }
        };
    }

    private static TInput GetInput<TInput>(FlowMapContext context)
        => context.Variables.TryGetValue("input", out var value) && value is TInput input
            ? input
            : throw new InvalidOperationException(
                $"Sample expression expected input type '{typeof(TInput).Name}'.");

    private static long ReadNumber(object? value)
        => value switch
        {
            null => 0,
            long number => number,
            int number => number,
            JsonElement json when json.ValueKind == JsonValueKind.Number &&
                                  json.TryGetInt64(out var number) => number,
            _ => throw new InvalidOperationException(
                $"Cannot read '{value?.GetType().Name ?? "null"}' as a number.")
        };
}

internal sealed class TimerTickContextFactory : IFlowMapContextFactory<TimerTick>
{
    public FlowMapContext Create(TimerTick input)
        => new()
        {
            Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["input"] = input,
                ["value"] = input,
                ["name"] = input.Name,
                ["sequence"] = input.Sequence,
                ["timestamp"] = input.Timestamp,
                ["dueAt"] = input.DueAt,
                ["interval"] = input.Interval
            }
        };
}
