using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Nodes;

/// <summary>
/// A bounded live output that applies backpressure and delivers every accepted item to
/// every subscriber that was active when the item was accepted.
/// </summary>
/// <remarks>
/// Acceptance is an in-process guarantee only. It does not persist items or survive a
/// process failure. Items accepted while no subscribers are active are discarded rather
/// than retained for replay.
/// </remarks>
public sealed class FlowOutput<T> : ISourceBlock<T>, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Queue<Delivery> _queue = [];
    private readonly SemaphoreSlim _availableSlots;
    private readonly SemaphoreSlim _availableItems = new(0);
    private readonly CancellationTokenSource _acceptanceStopped = new();
    private readonly CancellationTokenSource _deliveryStopped = new();
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _pump;
    private FanoutLink[] _links = [];
    private LifecycleState _state = LifecycleState.Accepting;
    private Exception? _fault;
    private int _disposeStarted;

    public FlowOutput(FlowOutputOptions? options = null)
    {
        options ??= new FlowOutputOptions();
        if (options.Capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Capacity must be greater than zero.");
        }

        _availableSlots = new SemaphoreSlim(options.Capacity, options.Capacity);
        _pump = PumpAsync();
    }

    /// <summary>
    /// Completes after every accepted item has been dispatched, or faults when reliable
    /// delivery can no longer be honored.
    /// </summary>
    public Task Completion => _completion.Task;

    /// <summary>
    /// Waits for bounded queue capacity and accepts ownership of <paramref name="value"/>.
    /// A successful result does not mean downstream processing has completed.
    /// </summary>
    public async ValueTask<bool> SendAsync(
        T value,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsAccepting())
        {
            return false;
        }

        CancellationTokenSource? linkedCancellation = null;
        try
        {
            var waitToken = _acceptanceStopped.Token;
            if (cancellationToken.CanBeCanceled)
            {
                linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    _acceptanceStopped.Token);
                waitToken = linkedCancellation.Token;
            }

            await _availableSlots.WaitAsync(waitToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested &&
            _acceptanceStopped.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            linkedCancellation?.Dispose();
        }

        lock (_gate)
        {
            if (_state != LifecycleState.Accepting)
            {
                _availableSlots.Release();
                return false;
            }

            _queue.Enqueue(new Delivery(value, _links));
            _availableItems.Release();
            return true;
        }
    }

    public IDisposable LinkTo(
        ITargetBlock<T> target,
        DataflowLinkOptions linkOptions)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(linkOptions);

        LifecycleState terminalState;
        Exception? terminalFault;
        lock (_gate)
        {
            terminalState = _state;
            terminalFault = _fault;
            if (terminalState is LifecycleState.Accepting or LifecycleState.Completing)
            {
                var link = new FanoutLink(
                    target,
                    linkOptions.PropagateCompletion,
                    linkOptions.MaxMessages,
                    _deliveryStopped.Token,
                    RemoveLink);
                _links = linkOptions.Append
                    ? [.. _links, link]
                    : [link, .. _links];
                return link;
            }
        }

        PropagateTerminalState(target, linkOptions.PropagateCompletion, terminalState, terminalFault);
        return EmptyDisposable.Instance;
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
        var transitioned = false;
        lock (_gate)
        {
            if (_state == LifecycleState.Accepting)
            {
                _state = LifecycleState.Completing;
                transitioned = true;
            }
        }

        if (!transitioned)
        {
            return;
        }

        _acceptanceStopped.Cancel();
        _availableItems.Release();
    }

    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        TransitionToFault(exception);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        Complete();
        try
        {
            await _pump.ConfigureAwait(false);
        }
        finally
        {
            foreach (var link in SnapshotLinks())
            {
                link.Dispose();
            }
        }
    }

    private bool IsAccepting()
    {
        lock (_gate)
        {
            return _state == LifecycleState.Accepting;
        }
    }

    private async Task PumpAsync()
    {
        try
        {
            while (true)
            {
                await _availableItems
                    .WaitAsync(_deliveryStopped.Token)
                    .ConfigureAwait(false);

                Delivery? delivery;
                lock (_gate)
                {
                    if (_state == LifecycleState.Faulted)
                    {
                        throw _fault ?? new InvalidOperationException(
                            "Reliable output delivery faulted.");
                    }

                    if (_queue.Count == 0)
                    {
                        if (_state == LifecycleState.Completing)
                        {
                            _state = LifecycleState.Finalizing;
                            break;
                        }

                        continue;
                    }

                    delivery = _queue.Dequeue();
                }

                _availableSlots.Release();
                await DeliverAsync(delivery).ConfigureAwait(false);
            }

            CompleteLinkedTargets();
            lock (_gate)
            {
                _state = LifecycleState.Completed;
            }

            _completion.TrySetResult();
        }
        catch (OperationCanceledException) when (_deliveryStopped.IsCancellationRequested)
        {
            CompleteFaultedPump();
        }
        catch (Exception exception)
        {
            TransitionToFault(Unwrap(exception));
            CompleteFaultedPump();
        }
    }

    private async Task DeliverAsync(Delivery delivery)
    {
        if (delivery.Links.Length == 0)
        {
            return;
        }

        if (delivery.Links.Length == 1)
        {
            await DeliverToLinkAsync(delivery.Links[0], delivery.Value).ConfigureAwait(false);
            return;
        }

        var deliveries = new Task[delivery.Links.Length];
        for (var index = 0; index < delivery.Links.Length; index++)
        {
            deliveries[index] = DeliverToLinkAsync(delivery.Links[index], delivery.Value);
        }

        await Task.WhenAll(deliveries).ConfigureAwait(false);
    }

    private async Task DeliverToLinkAsync(FanoutLink link, T value)
    {
        if (link.IsDisposed)
        {
            return;
        }

        try
        {
            var accepted = await link.Target
                .SendAsync(value, link.CancellationToken)
                .ConfigureAwait(false);
            if (!accepted)
            {
                throw new InvalidOperationException(
                    "A reliable output target stopped accepting messages.");
            }

            link.MarkDelivered();
        }
        catch (OperationCanceledException) when (link.IsDisposed)
        {
        }
        catch (ObjectDisposedException) when (link.IsDisposed)
        {
        }
        catch (Exception exception)
        {
            TransitionToFault(Unwrap(exception));
            throw;
        }
    }

    private void TransitionToFault(Exception exception)
    {
        var transitioned = false;
        lock (_gate)
        {
            if (_state is LifecycleState.Completed or LifecycleState.Faulted)
            {
                return;
            }

            _state = LifecycleState.Faulted;
            _fault = exception;
            _queue.Clear();
            transitioned = true;
        }

        if (!transitioned)
        {
            return;
        }

        _acceptanceStopped.Cancel();
        _deliveryStopped.Cancel();
        _availableItems.Release();
    }

    private void CompleteFaultedPump()
    {
        Exception fault;
        lock (_gate)
        {
            fault = _fault ?? new InvalidOperationException(
                "Reliable output delivery stopped unexpectedly.");
        }

        FaultLinkedTargets(fault);
        _completion.TrySetException(fault);
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
                try
                {
                    link.Target.Fault(exception);
                }
                catch
                {
                    // The reliable output fault remains the authoritative failure.
                }
            }
        }
    }

    private FanoutLink[] SnapshotLinks()
    {
        lock (_gate)
        {
            return _links;
        }
    }

    private void RemoveLink(FanoutLink link)
    {
        lock (_gate)
        {
            var index = Array.IndexOf(_links, link);
            if (index < 0)
            {
                return;
            }

            if (_links.Length == 1)
            {
                _links = [];
                return;
            }

            var updated = new FanoutLink[_links.Length - 1];
            Array.Copy(_links, 0, updated, 0, index);
            Array.Copy(_links, index + 1, updated, index, _links.Length - index - 1);
            _links = updated;
        }
    }

    private static void PropagateTerminalState(
        ITargetBlock<T> target,
        bool propagateCompletion,
        LifecycleState state,
        Exception? fault)
    {
        if (!propagateCompletion)
        {
            return;
        }

        if (state == LifecycleState.Faulted)
        {
            target.Fault(fault ?? new InvalidOperationException(
                "Reliable output delivery faulted."));
            return;
        }

        target.Complete();
    }

    private static Exception Unwrap(Exception exception)
        => exception is AggregateException aggregate
            ? aggregate.GetBaseException()
            : exception;

    private sealed record Delivery(T Value, FanoutLink[] Links);

    private sealed class FanoutLink : IDisposable
    {
        private readonly CancellationTokenSource _stopped;
        private readonly Action<FanoutLink> _remove;
        private long _remainingMessages;
        private int _disposed;

        public FanoutLink(
            ITargetBlock<T> target,
            bool propagateCompletion,
            long maxMessages,
            CancellationToken outputStopped,
            Action<FanoutLink> remove)
        {
            Target = target;
            PropagateCompletion = propagateCompletion;
            _remainingMessages = maxMessages;
            _stopped = CancellationTokenSource.CreateLinkedTokenSource(outputStopped);
            _remove = remove;
        }

        public ITargetBlock<T> Target { get; }

        public bool PropagateCompletion { get; }

        public CancellationToken CancellationToken => _stopped.Token;

        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        public void MarkDelivered()
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
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                _stopped.Cancel();
            }
            finally
            {
                _remove(this);
                _stopped.Dispose();
            }
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static EmptyDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }

    private enum LifecycleState
    {
        Accepting,
        Completing,
        Finalizing,
        Completed,
        Faulted
    }
}
