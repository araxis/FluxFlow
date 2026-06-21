using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;

namespace FluxFlow.Composition;

public abstract class CompositionOutputPort
{
    private protected CompositionOutputPort(string name, Type messageType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        MessageType = messageType ?? throw new ArgumentNullException(nameof(messageType));
    }

    public string Name { get; }

    public Type MessageType { get; }

    internal abstract bool TryLinkTo(CompositionInputPort input, out IDisposable? link);
}

public sealed class CompositionOutputPort<TMessage> : CompositionOutputPort
{
    public CompositionOutputPort(string name, ISourceBlock<FlowMessage<TMessage>> source)
        : base(name, typeof(TMessage))
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public ISourceBlock<FlowMessage<TMessage>> Source { get; }

    internal override bool TryLinkTo(CompositionInputPort input, out IDisposable? link)
    {
        if (input is not CompositionInputPort<TMessage> typedInput)
        {
            link = null;
            return false;
        }

        link = Source.LinkTo(
            typedInput.Target,
            new DataflowLinkOptions { PropagateCompletion = true });
        return true;
    }
}
