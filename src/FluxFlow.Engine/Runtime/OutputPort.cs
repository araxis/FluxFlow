using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Mapping;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Engine.Runtime;

public abstract class OutputPort : IDisposable, IAsyncDisposable
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

    /// <summary>
    /// Raised when a message could not be delivered over a specific link:
    /// either a conditional link predicate threw (the message is dropped for
    /// that link) or a linked input rejected the message because it already
    /// completed (the link is detached). Sibling links are unaffected.
    /// </summary>
    public event Action<OutputPortLinkFailure>? LinkFailed;

    private protected void ReportLinkFailure(OutputPortLinkFailure failure)
        => LinkFailed?.Invoke(failure);

    public abstract IDisposable? TryLinkTo(
        InputPort input,
        bool propagateCompletion,
        out ApplicationRuntimeBuildError? error);

    public virtual IDisposable? TryLinkTo(
        InputPort input,
        bool propagateCompletion,
        IFlowPredicate<object?>? condition,
        out ApplicationRuntimeBuildError? error)
    {
        if (condition is null)
        {
            return TryLinkTo(input, propagateCompletion, out error);
        }

        error = new(
            ApplicationRuntimeBuildErrorCode.LinkFailed,
            $"Output port '{Address}' does not support conditional links.",
            PortName: input.Address.Port);
        return null;
    }

    public abstract IDisposable LinkToDiscard();

    public virtual void Dispose()
    {
    }

    public virtual ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed class OutputPort<T> : OutputPort
{
    private readonly BufferBlock<T> _queue;
    private readonly IDisposable _sourceLink;
    private readonly CancellationTokenSource _disposeToken = new();
    private Task? _pump;
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();
    private readonly List<FanoutLink> _links = [];
    private int _disposed;

    public override Task Completion => _completion.Task;
    public override bool DrainWhenUnlinked { get; }

    public OutputPort(
        PortAddress address,
        ISourceBlock<T> source,
        bool drainWhenUnlinked = true)
        : base(address, typeof(T))
    {
        ArgumentNullException.ThrowIfNull(source);
        _queue = new BufferBlock<T>(new DataflowBlockOptions { BoundedCapacity = 1 });
        _sourceLink = source.LinkTo(
            _queue,
            new DataflowLinkOptions { PropagateCompletion = true });
        DrainWhenUnlinked = drainWhenUnlinked;
    }

    public override IDisposable? TryLinkTo(
        InputPort input,
        bool propagateCompletion,
        out ApplicationRuntimeBuildError? error)
        => TryLinkTo(input, propagateCompletion, condition: null, out error);

    public override IDisposable? TryLinkTo(
        InputPort input,
        bool propagateCompletion,
        IFlowPredicate<object?>? condition,
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
            FanoutLink link;
            lock (_gate)
            {
                ThrowIfDisposed();
                link = new FanoutLink(
                    typedInput.Target,
                    propagateCompletion,
                    condition,
                    RemoveLink);
                _links.Add(link);
                StartPumpIfNeeded();
            }

            if (Completion.IsCompleted)
            {
                // The pump finished before this link was added; propagate the
                // outcome directly so the target is not left dangling.
                link.Dispose();
                if (propagateCompletion)
                {
                    if (Completion.IsFaulted)
                    {
                        typedInput.Target.Fault(
                            Completion.Exception?.InnerException ?? Completion.Exception!);
                    }
                    else
                    {
                        typedInput.Target.Complete();
                    }
                }
            }

            error = null;
            return link;
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
    {
        FanoutLink link;
        lock (_gate)
        {
            ThrowIfDisposed();
            link = new FanoutLink(
                DataflowBlock.NullTarget<T>(),
                propagateCompletion: false,
                condition: null,
                RemoveLink);
            _links.Add(link);
            StartPumpIfNeeded();
        }

        return link;
    }

    public override void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        StopPump();
        var pump = SnapshotPump();
        try
        {
            if (pump is null)
            {
                _completion.TrySetResult();
            }
            else
            {
                pump.GetAwaiter().GetResult();
            }
        }
        finally
        {
            _disposeToken.Dispose();
        }
    }

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        StopPump();
        var pump = SnapshotPump();
        try
        {
            if (pump is null)
            {
                _completion.TrySetResult();
            }
            else
            {
                await pump.ConfigureAwait(false);
            }
        }
        finally
        {
            _disposeToken.Dispose();
        }
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _queue.OutputAvailableAsync(cancellationToken).ConfigureAwait(false))
            {
                while (_queue.TryReceive(out var item))
                {
                    var links = SnapshotLinks();
                    if (links.Length == 0)
                    {
                        continue;
                    }

                    if (links.Length == 1)
                    {
                        await SendToLinkAsync(links[0], item).ConfigureAwait(false);
                        continue;
                    }

                    var sends = new Task[links.Length];
                    for (var i = 0; i < links.Length; i++)
                    {
                        sends[i] = SendToLinkAsync(links[i], item);
                    }

                    await Task.WhenAll(sends).ConfigureAwait(false);
                }
            }

            // Surface a faulted source to linked targets instead of downgrading
            // the fault to a normal completion.
            await _queue.Completion.ConfigureAwait(false);

            _completion.TrySetResult();
            CompleteLinkedTargets();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _completion.TrySetResult();
        }
        catch (Exception exception)
        {
            var unwrapped = exception is AggregateException aggregate
                ? aggregate.GetBaseException()
                : exception;
            _completion.TrySetException(unwrapped);
            FaultLinkedTargets(unwrapped);
        }
        finally
        {
            _sourceLink.Dispose();
        }
    }

    private void StopPump()
    {
        _sourceLink.Dispose();
        _disposeToken.Cancel();
        _queue.Complete();
        foreach (var link in SnapshotLinks())
        {
            link.Dispose();
        }
    }

    private Task? SnapshotPump()
    {
        lock (_gate)
        {
            return _pump;
        }
    }

    private void StartPumpIfNeeded()
    {
        _pump ??= PumpAsync(_disposeToken.Token);
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private async Task SendToLinkAsync(FanoutLink link, T item)
    {
        if (link.IsDisposed)
        {
            return;
        }

        try
        {
            if (!link.IsMatch(item))
            {
                return;
            }
        }
        catch (Exception exception)
        {
            // A throwing link condition drops the message for this link only;
            // other links and later messages are unaffected.
            ReportLinkFailure(new OutputPortLinkFailure
            {
                Port = Address,
                Reason = OutputPortLinkFailureReason.ConditionFailed,
                Exception = exception
            });
            return;
        }

        try
        {
            var accepted = await link.Target.SendAsync(item, link.CancellationToken).ConfigureAwait(false);
            if (!accepted && !link.IsDisposed)
            {
                // The target completed or faulted; detach it so the remaining
                // links keep receiving messages instead of faulting the port.
                link.Dispose();
                ReportLinkFailure(new OutputPortLinkFailure
                {
                    Port = Address,
                    Reason = OutputPortLinkFailureReason.TargetRejected
                });
            }
        }
        catch (OperationCanceledException) when (link.IsDisposed)
        {
            return;
        }
        catch (ObjectDisposedException) when (link.IsDisposed)
        {
            return;
        }
    }

    private FanoutLink[] SnapshotLinks()
    {
        lock (_gate)
        {
            return _links.ToArray();
        }
    }

    private void RemoveLink(FanoutLink link)
    {
        lock (_gate)
        {
            _links.Remove(link);
        }
    }

    private void CompleteLinkedTargets()
    {
        foreach (var link in SnapshotLinks())
        {
            if (!link.IsDisposed && link.PropagateCompletion)
            {
                link.Target.Complete();
            }
        }
    }

    private void FaultLinkedTargets(Exception exception)
    {
        foreach (var link in SnapshotLinks())
        {
            if (!link.IsDisposed && link.PropagateCompletion)
            {
                link.Target.Fault(exception);
            }
        }
    }

    private sealed class FanoutLink : IDisposable
    {
        private readonly CancellationTokenSource _disposedToken = new();
        private readonly CancellationToken _cancellationToken;
        private readonly Action<FanoutLink> _remove;
        private int _disposed;

        public FanoutLink(
            ITargetBlock<T> target,
            bool propagateCompletion,
            IFlowPredicate<object?>? condition,
            Action<FanoutLink> remove)
        {
            Target = target;
            PropagateCompletion = propagateCompletion;
            Condition = condition;
            _remove = remove;
            _cancellationToken = _disposedToken.Token;
        }

        public ITargetBlock<T> Target { get; }
        public bool PropagateCompletion { get; }
        public IFlowPredicate<object?>? Condition { get; }
        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;
        public CancellationToken CancellationToken => _cancellationToken;

        public bool IsMatch(T item)
            => Condition is null || Condition.IsMatch(item);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                try
                {
                    _disposedToken.Cancel();
                }
                finally
                {
                    _remove(this);
                    _disposedToken.Dispose();
                }
            }
        }
    }
}
