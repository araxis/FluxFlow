using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Components.Routing;

/// <summary>Forwards messages while open; discards them while closed. Starts closed by default.</summary>
public sealed class Gate : FlowTransformNode
{
    private bool _isOpen;

    public Gate(bool initiallyOpen = false, ExecutionDataflowBlockOptions? options = null, int outputCapacity = 128)
        : base(options, outputCapacity)
    {
        _isOpen = initiallyOpen;
        Open = AddInput(_ => { Volatile.Write(ref _isOpen, true); return Task.CompletedTask; });
        Close = AddInput(_ => { Volatile.Write(ref _isOpen, false); return Task.CompletedTask; });
    }

    public bool IsOpen => Volatile.Read(ref _isOpen);
    public ITargetBlock<FlowMessage> Open { get; }
    public ITargetBlock<FlowMessage> Close { get; }

    // Gate state applies to both normal messages and error envelopes.
    protected override bool HandlesErrors => true;

    protected override Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
        => Task.FromResult<IEnumerable<FlowMessage>>(IsOpen ? [message] : []);
}
