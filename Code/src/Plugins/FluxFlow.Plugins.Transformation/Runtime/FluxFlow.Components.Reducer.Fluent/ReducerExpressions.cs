using FluxFlow.Core.Mapping;
using FluxFlow.Core.Messaging;

namespace FluxFlow.Components.Reducer;

public static class ReducerExpressions
{
    public static Func<FlowValue, FlowValue, FlowValue> CompileReduce(IFlowExpressionEngine engine, string expression)
    {
        var reduce = engine.Compile<FlowValue>(expression,
            new Dictionary<string, Type> { ["state"] = typeof(FlowValue), ["input"] = typeof(FlowValue) })
            ?? throw new InvalidOperationException($"Expression engine '{engine.Name}' returned no compiled expression.");
        return (state, input) => reduce(new FlowMapContext
        {
            Variables = new Dictionary<string, object?> { ["state"] = state, ["input"] = input, ["value"] = input, ["payload"] = input }
        });
    }

    public static Func<FlowValue, FlowValue, bool> CompileEquality(IFlowExpressionEngine engine, string expression)
    {
        var equal = engine.Compile<bool>(expression,
            new Dictionary<string, Type> { ["previous"] = typeof(FlowValue), ["current"] = typeof(FlowValue) })
            ?? throw new InvalidOperationException($"Expression engine '{engine.Name}' returned no compiled expression.");
        return (previous, current) => equal(new FlowMapContext
        {
            Variables = new Dictionary<string, object?> { ["previous"] = previous, ["current"] = current }
        });
    }
}
