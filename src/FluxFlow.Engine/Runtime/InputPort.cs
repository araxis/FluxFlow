using FluxFlow.Engine.Definitions;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Engine.Runtime;

public abstract class InputPort
{
    private protected InputPort(PortAddress address, Type valueType)
    {
        Address = address;
        ValueType = valueType;
    }

    public PortAddress Address { get; }
    public Type ValueType { get; }
    public abstract Task Completion { get; }

    public abstract void Complete();

    public abstract void Fault(Exception exception);
}

public sealed class InputPort<T>(PortAddress address, ITargetBlock<T> target) : InputPort(address, typeof(T))
{
    public ITargetBlock<T> Target { get; } = target;
    public override Task Completion => Target.Completion;

    public override void Complete() => Target.Complete();

    public override void Fault(Exception exception) => Target.Fault(exception);
}
