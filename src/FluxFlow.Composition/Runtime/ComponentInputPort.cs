using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;

namespace FluxFlow.Composition;

public abstract class ComponentInputPort
{
    private protected ComponentInputPort(
        string name,
        Type messageType,
        ComponentPortKind kind = ComponentPortKind.Message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        Name = name;
        MessageType = messageType ?? throw new ArgumentNullException(nameof(messageType));
        Kind = kind;
    }

    public string Name { get; }

    public Type MessageType { get; }

    public ComponentPortKind Kind { get; }

    internal abstract void Complete();

    internal abstract void Fault(Exception exception);
}

public sealed class ComponentInputPort<TMessage> : ComponentInputPort
{
    public ComponentInputPort(string name, ITargetBlock<FlowMessage<TMessage>> target)
        : base(name, typeof(TMessage))
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
    }

    public ITargetBlock<FlowMessage<TMessage>> Target { get; }

    internal override void Complete() => Target.Complete();

    internal override void Fault(Exception exception)
        => ((IDataflowBlock)Target).Fault(exception);
}

public sealed class ComponentSignalInputPort : ComponentInputPort
{
    public ComponentSignalInputPort(string name, IFlowSignalTarget target)
        : base(name, typeof(object), ComponentPortKind.Signal)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
    }

    public IFlowSignalTarget Target { get; }

    internal override void Complete()
    {
    }

    internal override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
    }
}
