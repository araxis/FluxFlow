using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Components.ChangeFilter;
/// <summary>Forwards the original message only when its selected key changes.</summary>
public sealed class ChangeFilter<TKey> : FlowNode where TKey : notnull
{
    public ITargetBlock<FlowMessage> Input { get; }

    private readonly Func<FlowMessage, TKey> _select;
    private readonly Func<TKey, TKey, bool> _areEqual;
    private TKey _previous = default!;
    private bool _hasPrevious;
    public ChangeFilter(ChangeFilterOptions<TKey> options)
    {
        Validate(options);
        (_select, _areEqual) = (options.KeySelector, options.AreEqual);
        var execution = options.Execution ?? new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
        var outputOptions = new DataflowBlockOptions { BoundedCapacity = options.OutputCapacity };
        Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
        Events = new BroadcastBlock<FlowMessage>(static message => message);
        Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
        var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, Transform, Output, Stopping), execution);
        input.LinkTo(Output);
        Input = input;
        Lifetime.HasInput(input);
        Lifetime.HasOutput(Output);
        Lifetime.HasEvents(Events);
        Lifetime.Observe();
    }

    private IEnumerable<FlowMessage> Transform(FlowMessage message)
    {
        var key = _select(message);
        ArgumentNullException.ThrowIfNull(key);
        if (_hasPrevious && _areEqual(_previous, key))
            return [];
        _previous = key;
        _hasPrevious = true;
        return [message];
    }

    protected override ValueTask OnDisposeAsync()
    {
        _previous = default!;
        _hasPrevious = false;
        return ValueTask.CompletedTask;
    }

    private static void Validate(ChangeFilterOptions<TKey> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.KeySelector);
        ArgumentNullException.ThrowIfNull(options.AreEqual);
        if (options.Execution is { } execution && (execution.MaxDegreeOfParallelism != 1 || !execution.EnsureOrdered))
            throw new ArgumentException("ChangeFilter requires ordered serial processing.", nameof(options));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.OutputCapacity, nameof(options.OutputCapacity));
    }
}
