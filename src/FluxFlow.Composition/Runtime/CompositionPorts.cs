using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;

namespace FluxFlow.Composition;

public static class CompositionPorts
{
    public static CompositionInputPort<TMessage> Input<TMessage>(
        string name,
        ITargetBlock<FlowMessage<TMessage>> target)
        => new(name, target);

    public static CompositionOutputPort<TMessage> Output<TMessage>(
        string name,
        ISourceBlock<FlowMessage<TMessage>> source)
        => new(name, source);

    public static CompositionPortMetadata Metadata<TMessage>(string name)
        => CompositionPortMetadata.Create<TMessage>(name);
}
