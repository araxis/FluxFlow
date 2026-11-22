using FluxFlow.Core.Messaging;
using FluxFlow.Application.Addressing;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Application;

/// <summary>A disposable observation of output offers. Never consumes or reserves messages
/// from the observed output, and never waits for an observer to read samples.</summary>
public sealed class FlowOutputProbe : IDisposable
{
    private readonly BufferBlock<FlowMessage> _messages;
    private IDisposable? _link;
    private long _droppedCount;
    private int _disposed;

    internal FlowOutputProbe(ISourceBlock<FlowMessage> output, int capacity, PortAddress address)
    {
        Address = address;
        _messages = new BufferBlock<FlowMessage>(new DataflowBlockOptions { BoundedCapacity = capacity });
        _link = output.LinkTo(_messages,
            new DataflowLinkOptions { Append = false, PropagateCompletion = true }, CaptureOffer);
    }

    /// <summary>The observed port. Its component address matches flow.component.address in diagnostics.</summary>
    public PortAddress Address { get; }

    /// <summary>Original immutable messages, without serialization or a new envelope. Read with
    /// native Dataflow receive APIs. Offers can include retained-message replay or repeated offers;
    /// they do not confirm input admission or successful processing. Source completion/fault propagates here.</summary>
    public IReceivableSourceBlock<FlowMessage> Messages => _messages;

    /// <summary>Samples refused by the probe buffer while attached, normally because it is full.
    /// Business delivery is unaffected. Discarded samples during disposal are not counted.</summary>
    public long DroppedCount => Interlocked.Read(ref _droppedCount);

    private bool CaptureOffer(FlowMessage message)
    {
        if (Volatile.Read(ref _disposed) == 0 && !_messages.Post(message)
            && Volatile.Read(ref _disposed) == 0)
            Interlocked.Increment(ref _droppedCount);
        // A false native link predicate declines the original offer without consuming it.
        return false;
    }

    /// <summary>Detaches once, closes the probe buffer and discards unread available samples.
    /// Already received samples remain with their readers. Does not complete or fault the observed output.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { Interlocked.Exchange(ref _link, null)?.Dispose(); }
        finally
        {
            _messages.Complete();
            _messages.TryReceiveAll(out _);
        }
    }
}
