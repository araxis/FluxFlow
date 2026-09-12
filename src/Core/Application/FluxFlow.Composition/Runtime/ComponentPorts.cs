using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;

namespace FluxFlow.Composition;

public static class ComponentPorts
{
    public static ComponentInputPort Input(string name, ITargetBlock<FlowMessage> target)
        => new(name, target);

    public static ComponentInputPort SignalInput(string name, ITargetBlock<FlowMessage> target)
        => new(name, target, ComponentPortKind.Signal);

    public static ComponentOutputPort Output(string name, ISourceBlock<FlowMessage> source)
        => new(name, source);

    public static ComponentEventSource Events(
        string name,
        ISourceBlock<FlowMessage> source)
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
