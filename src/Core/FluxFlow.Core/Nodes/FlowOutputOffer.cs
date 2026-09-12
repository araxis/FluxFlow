using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Nodes;

/// <summary>Forwards one live offer's callbacks to the native asynchronous sender.</summary>
internal sealed class FlowOutputOffer<T>(
    ISourceBlock<T> owner,
    ITargetBlock<T> target,
    DataflowMessageHeader header) : ITargetBlock<T>
{
    private ISourceBlock<T>? _sender;
    private DataflowMessageHeader _senderHeader;
    private int _finished;

    public DataflowMessageHeader Header { get; } = header;
    public bool WasDeclined { get; private set; }
    public Task Completion => target.Completion;

    public Task<bool> SendAsync(T value, CancellationToken cancellationToken)
        => DataflowBlock.SendAsync(this, value, cancellationToken);

    internal void Finish() => Volatile.Write(ref _finished, 1);

    public T Consume(out bool consumed)
    {
        var sender = Volatile.Read(ref _sender);
        if (sender is not null)
            return sender.ConsumeMessage(_senderHeader, this, out consumed)!;
        consumed = false;
        return default!;
    }

    public bool Reserve()
        => Volatile.Read(ref _sender)?.ReserveMessage(_senderHeader, this) ?? false;

    public void Release()
    {
        var sender = Volatile.Read(ref _sender)
            ?? throw new InvalidOperationException("The offer has no reservation.");
        sender.ReleaseReservation(_senderHeader, this);
    }

    DataflowMessageStatus ITargetBlock<T>.OfferMessage(
        DataflowMessageHeader messageHeader,
        T messageValue,
        ISourceBlock<T>? source,
        bool consumeToAccept)
    {
        if (Volatile.Read(ref _finished) != 0)
            return DataflowMessageStatus.DecliningPermanently;

        // Request the native sender's slow path before exposing an offer to a target.
        // A filtered link needs a real source even when it can accept immediately.
        if (source is null)
            return DataflowMessageStatus.Declined;

        _senderHeader = messageHeader;
        Volatile.Write(ref _sender, source);
        var status = target.OfferMessage(Header, messageValue, owner, consumeToAccept);
        WasDeclined = status == DataflowMessageStatus.Declined;
        return status;
    }

    void IDataflowBlock.Complete() => target.Complete();
    void IDataflowBlock.Fault(Exception exception) => target.Fault(exception);
}
