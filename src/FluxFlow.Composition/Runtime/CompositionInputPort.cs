using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;

namespace FluxFlow.Composition;

public abstract class CompositionInputPort
{
    private protected CompositionInputPort(
        string name,
        Type messageType,
        CompositionPortKind kind = CompositionPortKind.Message)
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

    public CompositionPortKind Kind { get; }

    internal abstract void Complete();

    internal abstract void Fault(Exception exception);
}

public sealed class CompositionInputPort<TMessage> : CompositionInputPort
{
    public CompositionInputPort(string name, ITargetBlock<FlowMessage<TMessage>> target)
        : base(name, typeof(TMessage))
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
    }

    public ITargetBlock<FlowMessage<TMessage>> Target { get; }

    internal override void Complete() => Target.Complete();

    internal override void Fault(Exception exception)
        => ((IDataflowBlock)Target).Fault(exception);
}

public sealed class CompositionSignalInputPort : CompositionInputPort
{
    public CompositionSignalInputPort(string name, IFlowSignalTarget target)
        : base(name, typeof(object), CompositionPortKind.Signal)
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
