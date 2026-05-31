using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Engine.Runtime;

public abstract class OutputPort
{
    private protected OutputPort(PortAddress address, Type valueType)
    {
        Address = address;
        ValueType = valueType;
    }

    public PortAddress Address { get; }
    public Type ValueType { get; }
    public abstract Task Completion { get; }
    public abstract bool DrainWhenUnlinked { get; }

    public abstract IDisposable? TryLinkTo(
        InputPort input,
        bool propagateCompletion,
        out ApplicationRuntimeBuildError? error);

    public abstract IDisposable LinkToDiscard();
}

public sealed class OutputPort<T> : OutputPort
{
    private readonly BroadcastBlock<T> _broadcast;
    private readonly IDisposable _sourceLink;

    public ISourceBlock<T> Source => _broadcast;
    public override Task Completion => Source.Completion;
    public override bool DrainWhenUnlinked { get; }

    public OutputPort(
        PortAddress address,
        ISourceBlock<T> source,
        bool drainWhenUnlinked = true)
        : base(address, typeof(T))
    {
        _broadcast = new BroadcastBlock<T>(static value => value);
        _sourceLink = source.LinkTo(
            _broadcast,
            new DataflowLinkOptions { PropagateCompletion = true });
        DrainWhenUnlinked = drainWhenUnlinked && typeof(T) != typeof(FlowError);
    }

    public override IDisposable? TryLinkTo(
        InputPort input,
        bool propagateCompletion,
        out ApplicationRuntimeBuildError? error)
    {
        if (input is not InputPort<T> typedInput)
        {
            error = new(
                ApplicationRuntimeBuildErrorCode.PortTypeMismatch,
                $"Cannot link '{Address}' ({ValueType.Name}) to '{input.Address}' ({input.ValueType.Name}).",
                PortName: input.Address.Port);
            return null;
        }

        try
        {
            error = null;
            return Source.LinkTo(
                typedInput.Target,
                new DataflowLinkOptions { PropagateCompletion = propagateCompletion });
        }
        catch (Exception exception)
        {
            error = new(
                ApplicationRuntimeBuildErrorCode.LinkFailed,
                $"Failed to link '{Address}' to '{input.Address}': {exception.Message}",
                PortName: input.Address.Port);
            return null;
        }
    }

    public override IDisposable LinkToDiscard()
        => Source.LinkTo(
            DataflowBlock.NullTarget<T>(),
            new DataflowLinkOptions { PropagateCompletion = true });
}
