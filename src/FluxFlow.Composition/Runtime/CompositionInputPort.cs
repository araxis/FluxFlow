using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;

namespace FluxFlow.Composition;

public abstract class CompositionInputPort
{
    private protected CompositionInputPort(string name, Type messageType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        MessageType = messageType ?? throw new ArgumentNullException(nameof(messageType));
    }

    public string Name { get; }

    public Type MessageType { get; }
}

public sealed class CompositionInputPort<TMessage> : CompositionInputPort
{
    public CompositionInputPort(string name, ITargetBlock<FlowMessage<TMessage>> target)
        : base(name, typeof(TMessage))
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
    }

    public ITargetBlock<FlowMessage<TMessage>> Target { get; }
}
