using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Components.Source;
/// <summary>Each ordinary input emits the next sample value. Exhausted lists produce no output.</summary>
public sealed class Source : FlowNode
{
    public ITargetBlock<FlowMessage> Input { get; }

    private readonly FlowValue[] _values;
    private int _next;
    public Source(SourceOptions options)
    {
        _values = Snapshot(options);
        var execution = new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
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

    private IEnumerable<FlowMessage> Transform(FlowMessage message) => _next < _values.Length ? [message.WithValue(_values[_next++])] : [];
    private static FlowValue[] Snapshot(SourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Values);
        var values = options.Values.ToArray();
        if (values.Any(value => value is null))
            throw new ArgumentException("Use JsonFlowValue.Null for a JSON null value.", nameof(options));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.OutputCapacity, nameof(options.OutputCapacity));
        return values;
    }
}
