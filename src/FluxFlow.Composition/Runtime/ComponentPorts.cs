using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;

namespace FluxFlow.Composition;

public static class ComponentPorts
{
    public static ComponentInputPort<TMessage> Input<TMessage>(
        string name,
        ITargetBlock<FlowMessage<TMessage>> target)
        => new(name, target);

    public static ComponentSignalInputPort SignalInput(
        string name,
        IFlowSignalTarget target)
        => new(name, target);

    public static ComponentOutputPort<TMessage> Output<TMessage>(
        string name,
        ISourceBlock<FlowMessage<TMessage>> source)
        => new(name, source);

    public static ComponentEventSource Events(
        string name,
        ISourceBlock<FlowEvent> source)
        => new(name, source);

    public static ComponentPortMetadata Metadata<TMessage>(string name)
        => ComponentPortMetadata.Create<TMessage>(name);

    public static ComponentPortMetadata Metadata<TMessage>(
        string name,
        ComponentPortLinkCardinality linkCardinality)
        => ComponentPortMetadata.Create<TMessage>(name, linkCardinality);

    public static ComponentPortMetadata SignalMetadata(
        string name,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
        => ComponentPortMetadata.CreateSignal(name, linkCardinality);
}
