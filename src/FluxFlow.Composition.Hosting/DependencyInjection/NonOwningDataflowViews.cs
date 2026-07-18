using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;

namespace FluxFlow.Composition.Hosting.DependencyInjection;

internal sealed class DataflowBlockView(IDataflowBlock block) : IDataflowBlock
{
    public Task Completion => block.Completion;

    public void Complete() => block.Complete();

    public void Fault(Exception exception) => block.Fault(exception);
}

internal sealed class TargetBlockView<T>(ITargetBlock<T> target) : ITargetBlock<T>
{
    public Task Completion => target.Completion;

    public void Complete() => target.Complete();

    public void Fault(Exception exception) => target.Fault(exception);

    public DataflowMessageStatus OfferMessage(
        DataflowMessageHeader messageHeader,
        T messageValue,
        ISourceBlock<T>? source,
        bool consumeToAccept)
        => target.OfferMessage(messageHeader, messageValue, source, consumeToAccept);
}

internal sealed class SourceBlockView<T>(ISourceBlock<T> source) : ISourceBlock<T>
{
    public Task Completion => source.Completion;

    public void Complete() => source.Complete();

    public void Fault(Exception exception) => source.Fault(exception);

    public IDisposable LinkTo(
        ITargetBlock<T> target,
        DataflowLinkOptions linkOptions)
        => source.LinkTo(target, linkOptions);

    public T ConsumeMessage(
        DataflowMessageHeader messageHeader,
        ITargetBlock<T> target,
        out bool messageConsumed)
        => source.ConsumeMessage(messageHeader, target, out messageConsumed)!;

    public bool ReserveMessage(
        DataflowMessageHeader messageHeader,
        ITargetBlock<T> target)
        => source.ReserveMessage(messageHeader, target);

    public void ReleaseReservation(
        DataflowMessageHeader messageHeader,
        ITargetBlock<T> target)
        => source.ReleaseReservation(messageHeader, target);
}

internal sealed class FlowSignalTargetView(IFlowSignalTarget target) :
    IFlowSignalTarget
{
    public Task Completion => target.Completion;

    public ValueTask<bool> SendAsync<T>(
        FlowMessage<T> signal,
        CancellationToken cancellationToken = default)
        => target.SendAsync(signal, cancellationToken);
}
