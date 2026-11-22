using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Components.Reducer;
/// <summary>Maintains one immutable state and emits only changed states.</summary>
public sealed class Reducer : FlowNode
{
    public ITargetBlock<FlowMessage> Input { get; }

    private FlowValue _state;
    private readonly Func<FlowValue, FlowValue, FlowValue> _reduce;
    private readonly Func<FlowValue, FlowValue, bool> _areEqual;
    public Reducer(ReducerOptions options)
    {
        var execution = Validate(options).Execution ?? new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
        var outputCapacityValue = options.OutputCapacity;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
        var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
        Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
        Events = new BroadcastBlock<FlowMessage>(static message => message);
        Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
        var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, Transform, Output, Stopping), execution);
        input.LinkTo(Output);
        Input = input;
        Lifetime.HasInput(input);
        Lifetime.HasOutput(Output);
        Lifetime.HasEvents(Events);
        (_state, _reduce, _areEqual) = (options.InitialState, options.Reduce, options.AreEqual);
        Lifetime.Observe();
    }

    private IEnumerable<FlowMessage> Transform(FlowMessage message)
    {
        var candidate = _reduce(_state, message.Value) ?? throw new InvalidOperationException("Reduce must return a FlowValue, not null.");
        if (_areEqual(_state, candidate))
            return [];
        var result = message.WithValue(candidate);
        _state = candidate;
        return [result];
    }

    private static ReducerOptions Validate(ReducerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.InitialState);
        ArgumentNullException.ThrowIfNull(options.Reduce);
        ArgumentNullException.ThrowIfNull(options.AreEqual);
        if (options.Execution is { } execution && (execution.MaxDegreeOfParallelism != 1 || !execution.EnsureOrdered))
            throw new ArgumentException("Reducer requires MaxDegreeOfParallelism = 1 and EnsureOrdered = true.", nameof(options));
        return options;
    }
}
