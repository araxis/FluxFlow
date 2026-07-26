using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition.Addressing;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.Ports;

internal sealed class ApplicationOutputAttachment<T>(
    ApplicationOutputPort<T> owner,
    IDisposable link) : IDisposable
{
    private int _disposed;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        link.Dispose();
        owner.RemoveAttachment(this);
    }

    internal void ReportSourceFault(Exception exception)
        => owner.ReportSourceFault(exception);
}

internal sealed class ApplicationOutputRevision<T>(ApplicationOutputPort<T> owner) :
    IApplicationOutputRevision
{
    private int _disposed;

    public ApplicationAddress Address => owner.Address;

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            owner.ReleaseRevision();
        return ValueTask.CompletedTask;
    }
}

internal sealed class PreparedApplicationOutput<T> : IPreparedApplicationOutput
{
    private readonly ApplicationOutputPort<T> _owner;
    private readonly BufferBlock<FlowMessage<T>> _staging;
    private readonly Task _sourceCompletion;
    private readonly IDisposable _sourceLink;
    private IDisposable? _activeAttachment;
    private int _activated;
    private int _disposed;

    internal PreparedApplicationOutput(
        ApplicationOutputPort<T> owner,
        ISourceBlock<FlowMessage<T>> source)
    {
        _owner = owner;
        _staging = new BufferBlock<FlowMessage<T>>(new DataflowBlockOptions
        {
            BoundedCapacity = owner.Capacity
        });
        _sourceCompletion = source.Completion;
        _sourceLink = source.LinkTo(
            _staging,
            new DataflowLinkOptions { PropagateCompletion = true });
    }

    public ApplicationAddress Address => _owner.Address;

    public void ThrowIfFaulted()
    {
        var faultedCompletion = _sourceCompletion.IsFaulted
            ? _sourceCompletion
            : _staging.Completion.IsFaulted
                ? _staging.Completion
                : null;
        if (faultedCompletion is null)
            return;

        throw new InvalidOperationException(
            $"Prepared output source for '{Address}' faulted before activation.",
            faultedCompletion.Exception?.GetBaseException());
    }

    public void Activate()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _activated, 1) != 0)
            throw new InvalidOperationException($"Prepared output '{Address}' is already active.");
        _activeAttachment = _owner.Attach(_staging);
    }

    async ValueTask IPreparedApplicationOutput.DrainAsync(
        CancellationToken cancellationToken)
    {
        await _staging.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        await _owner.DrainAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _activeAttachment?.Dispose();
        _sourceLink.Dispose();
        _staging.Complete();
    }
}
