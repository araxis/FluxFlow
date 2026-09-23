using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Components.Reducer;

/// <summary>Maintains one immutable state and emits only changed states.</summary>
public sealed class Reducer : FlowTransformNode
{
    private FlowValue _state;
    private readonly Func<FlowValue, FlowValue, FlowValue> _reduce;
    private readonly Func<FlowValue, FlowValue, bool> _areEqual;

    public Reducer(ReducerOptions options) : base(Validate(options).Execution, options.OutputCapacity)
        => (_state, _reduce, _areEqual) = (options.InitialState, options.Reduce, options.AreEqual);

    protected override Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
    {
        var candidate = _reduce(_state, message.Value)
            ?? throw new InvalidOperationException("Reduce must return a FlowValue, not null.");
        if (_areEqual(_state, candidate)) return Task.FromResult<IEnumerable<FlowMessage>>([]);
        var result = message.WithValue(candidate);
        _state = candidate;
        return Task.FromResult<IEnumerable<FlowMessage>>([result]);
    }

    private static ReducerOptions Validate(ReducerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.InitialState);
        ArgumentNullException.ThrowIfNull(options.Reduce);
        ArgumentNullException.ThrowIfNull(options.AreEqual);
        if (options.Execution is { } execution &&
            (execution.MaxDegreeOfParallelism != 1 || !execution.EnsureOrdered))
            throw new ArgumentException("Reducer requires MaxDegreeOfParallelism = 1 and EnsureOrdered = true.", nameof(options));
        return options;
    }
}
