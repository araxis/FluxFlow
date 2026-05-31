using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Engine.Components;

internal sealed class FlowFanoutSource<T> : ISourceBlock<T>, IDisposable, IAsyncDisposable
{
    private readonly BufferBlock<T> _queue = new();
    private readonly CancellationTokenSource _disposed = new();
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();
    private readonly List<FanoutLink> _links = [];
    private readonly Task _pump;
    private int _stopped;
    private int _disposeStarted;

    public FlowFanoutSource()
    {
        _pump = PumpAsync(_disposed.Token);
    }

    public Task Completion => _completion.Task;

    public bool Post(T item)
    {
        if (Volatile.Read(ref _stopped) != 0)
        {
            return false;
        }

        return _queue.Post(item);
    }

    public Task<bool> SendAsync(
        T item,
        CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _stopped) != 0)
        {
            return Task.FromResult(false);
        }

        return _queue.SendAsync(item, cancellationToken);
    }

    public IDisposable LinkTo(
        ITargetBlock<T> target,
        DataflowLinkOptions linkOptions)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(linkOptions);

        if (Completion.IsCompleted)
        {
            PropagateCompletion(target, linkOptions.PropagateCompletion);
            return EmptyDisposable.Instance;
        }

        var link = new FanoutLink(
            target,
            linkOptions.PropagateCompletion,
            linkOptions.MaxMessages,
            RemoveLink);

        lock (_gate)
        {
            if (Completion.IsCompleted)
            {
                PropagateCompletion(target, linkOptions.PropagateCompletion);
                return EmptyDisposable.Instance;
            }

            if (linkOptions.Append)
            {
                _links.Add(link);
            }
            else
            {
                _links.Insert(0, link);
            }
        }

        if (Completion.IsCompleted)
        {
            link.Dispose();
            PropagateCompletion(target, linkOptions.PropagateCompletion);
        }

        return link;
    }

    public T ConsumeMessage(
        DataflowMessageHeader messageHeader,
        ITargetBlock<T> target,
        out bool messageConsumed)
    {
        messageConsumed = false;
        return default!;
    }

    public bool ReserveMessage(
        DataflowMessageHeader messageHeader,
        ITargetBlock<T> target)
        => false;

    public void ReleaseReservation(
        DataflowMessageHeader messageHeader,
        ITargetBlock<T> target)
    {
    }

    public void Complete()
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 0)
        {
            _queue.Complete();
        }
    }

    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (Interlocked.Exchange(ref _stopped, 1) == 0)
        {
            ((IDataflowBlock)_queue).Fault(exception);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _stopped, 1);
        _disposed.Cancel();
        foreach (var link in SnapshotLinks())
        {
            link.Dispose();
        }

        try
        {
            _pump.GetAwaiter().GetResult();
        }
        finally
        {
            _disposed.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _stopped, 1);
        _disposed.Cancel();
        foreach (var link in SnapshotLinks())
        {
            link.Dispose();
        }

        try
        {
            await _pump.ConfigureAwait(false);
        }
        finally
        {
            _disposed.Dispose();
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

                    await Task.WhenAll(links.Select(link => SendToLinkAsync(link, item))).ConfigureAwait(false);
                }
            }

            CompleteLinkedTargets();
            _completion.TrySetResult();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _completion.TrySetResult();
        }
        catch (Exception exception)
        {
            FaultLinkedTargets(exception);
            _completion.TrySetException(exception);
        }
    }

    private static async Task SendToLinkAsync(
        FanoutLink link,
        T item)
    {
        if (link.IsDisposed)
        {
            return;
        }

        try
        {
            var accepted = await link.Target.SendAsync(item, link.CancellationToken).ConfigureAwait(false);
            if (!accepted && !link.IsDisposed)
            {
                throw new InvalidOperationException("A linked diagnostics target rejected a message.");
            }

            if (accepted)
            {
                link.MarkMessageDelivered();
            }
        }
        catch (OperationCanceledException) when (link.IsDisposed)
        {
        }
        catch (ObjectDisposedException) when (link.IsDisposed)
        {
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

    private void PropagateCompletion(
        ITargetBlock<T> target,
        bool propagateCompletion)
    {
        if (!propagateCompletion)
        {
            return;
        }

        if (Completion.IsFaulted)
        {
            target.Fault(Completion.Exception?.InnerException ?? Completion.Exception!);
            return;
        }

        target.Complete();
    }

    private sealed class FanoutLink : IDisposable
    {
        private readonly CancellationTokenSource _disposedToken = new();
        private readonly Action<FanoutLink> _remove;
        private long _remainingMessages;
        private int _disposed;

        public FanoutLink(
            ITargetBlock<T> target,
            bool propagateCompletion,
            long maxMessages,
            Action<FanoutLink> remove)
        {
            Target = target;
            PropagateCompletion = propagateCompletion;
            _remainingMessages = maxMessages;
            _remove = remove;
        }

        public ITargetBlock<T> Target { get; }
        public bool PropagateCompletion { get; }
        public CancellationToken CancellationToken => _disposedToken.Token;
        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        public void MarkMessageDelivered()
        {
            if (Volatile.Read(ref _remainingMessages) <= 0)
            {
                return;
            }

            if (Interlocked.Decrement(ref _remainingMessages) == 0)
            {
                Dispose();
            }
        }

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

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
