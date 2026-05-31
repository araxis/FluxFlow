using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Engine.Runtime;

public static class RuntimeNodeFactoryContextExtensions
{
    public static RuntimeNodeBuilder CreateNode(
        this RuntimeNodeFactoryContext context,
        IFlowNode node)
        => new(context, node);

    public static PortAddress PortAddress(
        this RuntimeNodeFactoryContext context,
        string portName)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        return context.Address.Port(new PortName(portName));
    }

    public static InputPort<T> Input<T>(
        this RuntimeNodeFactoryContext context,
        string name,
        ITargetBlock<T> target)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(target);
        return new InputPort<T>(context.PortAddress(name), target);
    }

    public static OutputPort<T> Output<T>(
        this RuntimeNodeFactoryContext context,
        string name,
        ISourceBlock<T> source,
        bool drainWhenUnlinked = true)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(source);
        return new OutputPort<T>(context.PortAddress(name), source, drainWhenUnlinked);
    }
}
