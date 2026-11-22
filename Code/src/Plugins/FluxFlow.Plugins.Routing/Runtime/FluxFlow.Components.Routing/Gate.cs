using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Components.Routing;
/// <summary>Forwards messages while open; discards them while closed. Starts closed by default.</summary>
public sealed class Gate : FlowNode
{
    public ITargetBlock<FlowMessage> Input { get; }

    private bool _isOpen;
    public Gate(bool initiallyOpen = false, ExecutionDataflowBlockOptions? options = null, int outputCapacity = 128)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacity);
        _isOpen = initiallyOpen;
        var execution = options ?? new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
        var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacity };
        Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
        Events = new BroadcastBlock<FlowMessage>(static message => message);
        Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
        var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, Transform, Output, Stopping, handlesErrors: true), execution);
        input.LinkTo(Output);
        Input = input;
        Open = new ActionBlock<FlowMessage>(message => SetOpenAsync(message, true), execution);
        Close = new ActionBlock<FlowMessage>(message => SetOpenAsync(message, false), execution);
        Lifetime.HasInput(input);
        Lifetime.HasInput(Open);
        Lifetime.HasInput(Close);
        Lifetime.HasOutput(Output);
        Lifetime.HasEvents(Events);
        Lifetime.Observe();
    }

    public bool IsOpen => Volatile.Read(ref _isOpen);
    public ITargetBlock<FlowMessage> Open { get; }
    public ITargetBlock<FlowMessage> Close { get; }

    private Task SetOpenAsync(FlowMessage message, bool open) => message.ProcessMessageAsync(this, _ =>
    {
        Volatile.Write(ref _isOpen, open);
    }, Output, Stopping, handlesErrors: true);
    private IEnumerable<FlowMessage> Transform(FlowMessage message) => IsOpen ? [message] : [];
}
