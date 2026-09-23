using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Core.Nodes;

/// <summary>
/// A bounded live output that applies backpressure and delivers every accepted item to
/// every accepting link that was active when the item was accepted. Native filters may
/// decline individual items; a permanently declining target faults reliable delivery.
/// </summary>
/// <remarks>
/// Acceptance is an in-process guarantee only. It does not persist items or survive a
/// process failure. Items accepted while no subscribers are active are discarded rather
/// than retained for replay.
/// </remarks>
// The containing component owns cleanup. Keep pattern-based await using support,
// but do not let DI acquire ownership when this same block is registered as a port.
public sealed class FlowOutput<T> : ISourceBlock<T>
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _availableSlots;
    private readonly ActionBlock<object> _dispatcher;
    private readonly CancellationTokenSource _acceptanceStopped = new();
    private readonly CancellationTokenSource _deliveryStopped = new();
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _dispatchCompletion;
    private readonly bool _isolateTargetFailures;
    private readonly Action<Exception>? _deliveryFailure;
    private FanoutLink[] _links = [];
    private FanoutLink[] _activeLinks = [];
    private long _nextMessageHeader;
    private LifecycleState _state = LifecycleState.Accepting;
    private Exception? _fault;
    private int _disposeStarted;
    private Action<T>? _prepareRouting;
    private Action<Exception>? _routingFault;
    private Func<T, CancellationToken, ValueTask<FlowOutputDispatch<T>>>? _prepareDispatch;
    private int _activePreparedDispatch;

    public FlowOutput(FlowOutputOptions? options = null) : this(options, false) { }

    internal FlowOutput(FlowOutputOptions? options, bool capacityIncludesActive,
        bool isolateTargetFailures = false, Action<Exception>? deliveryFailure = null)
    {
        options ??= new FlowOutputOptions();
        if (options.Capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Capacity must be greater than zero.");
        }

        _isolateTargetFailures = isolateTargetFailures;
        _deliveryFailure = deliveryFailure;
        _availableSlots = new SemaphoreSlim(options.Capacity, options.Capacity);
        _dispatcher = new ActionBlock<object>(DispatchAsync, new ExecutionDataflowBlockOptions
        {
            // Capacity counts waiting items, in addition to one active fanout.
            // The admission gate also bounds the int.MaxValue case without overflow.
            BoundedCapacity = capacityIncludesActive ? options.Capacity : options.Capacity == int.MaxValue
                ? DataflowBlockOptions.Unbounded
                : options.Capacity + 1,
            MaxDegreeOfParallelism = 1
        });
        _dispatchCompletion = ObserveDispatchAsync();
    }

    /// <summary>
    /// Completes after every accepted item has been dispatched, or faults when reliable
    /// delivery can no longer be honored.
    /// </summary>
    public Task Completion => _completion.Task;

    // The graph uses this native ingress only for an external canonical source.
    // Standard components send directly through their existing output.
    internal ITargetBlock<object> Ingress => _dispatcher;

    internal int PendingPreparedMessages => _dispatcher.InputCount + Volatile.Read(ref _activePreparedDispatch);

    internal void ConfigureDispatch(Func<T, CancellationToken, ValueTask<FlowOutputDispatch<T>>> prepare)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        lock (_gate)
        {
            if (_prepareRouting is not null || _prepareDispatch is not null)
                throw new InvalidOperationException("An output can belong to only one graph route owner.");
            _prepareDispatch = prepare;
        }
    }

    // Component emission requires acceptance; preserve the underlying output fault on rejection.
    internal async ValueTask EmitAsync(T value, CancellationToken cancellationToken, string unavailableMessage)
    {
        if (await SendAsync(value, cancellationToken).ConfigureAwait(false)) return;
        if (Completion.IsFaulted) await Completion.ConfigureAwait(false);
        throw new InvalidOperationException(unavailableMessage);
    }

    internal async ValueTask DrainAsync(CancellationToken cancellationToken)
    {
        var barrier = new DrainBarrier();
        if (await _dispatcher.SendAsync(barrier, cancellationToken).ConfigureAwait(false))
        {
            var finished = await Task.WhenAny(barrier.Completion.Task, Completion)
                .WaitAsync(cancellationToken).ConfigureAwait(false);
            await finished.ConfigureAwait(false);
        }
        else
            await Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    internal void ConfigureRouting(Action<T> prepare, Action<Exception> fault)
    {
        lock (_gate)
        {
            if (_prepareRouting is not null || _prepareDispatch is not null)
                throw new InvalidOperationException("An output can belong to only one graph route owner.");
            _prepareRouting = prepare;
            _routingFault = fault;
        }
    }

    internal void ClearRouting()
    {
        lock (_gate)
        {
            _prepareRouting = null;
            _routingFault = null;
        }
    }

    internal IDisposable LinkRoute(ITargetBlock<T> target, Predicate<T> selected,
        bool rejectWhenFull, string rejectionMessage, Action<T, Exception>? onRejected = null)
    {
        lock (_gate)
        {
            var registration = LinkTo(target, new DataflowLinkOptions());
            // Admission snapshots use the same gate, so the policy is installed before
            // this new link can appear in a delivery. Graph selection is already resolved.
            if (registration is FanoutLink link)
                link.Route = new RoutePolicy(selected, rejectWhenFull, rejectionMessage, onRejected);
            return registration;
        }
    }

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

        return PostAccepted(value);
    }

    // Best-effort signal admission shares the same queue and capacity gate.
    internal bool Post(T value)
        => IsAccepting() && _availableSlots.Wait(0) && PostAccepted(value);

    private bool PostAccepted(T value)
    {
        lock (_gate)
        {
            if (_state != LifecycleState.Accepting)
            {
                _availableSlots.Release();
                return false;
            }

            if (_dispatcher.Post(new Delivery(value, _links, _prepareRouting)))
            {
                return true;
            }

            _availableSlots.Release();
            return false;
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
                    this,
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
        var offer = FindOffer(messageHeader, target);
        if (offer is not null)
            return offer.Consume(out messageConsumed);
        messageConsumed = false;
        return default!;
    }

    public bool ReserveMessage(
        DataflowMessageHeader messageHeader,
        ITargetBlock<T> target)
        => FindOffer(messageHeader, target)?.Reserve() ?? false;

    public void ReleaseReservation(
        DataflowMessageHeader messageHeader,
        ITargetBlock<T> target)
    {
        var offer = FindOffer(messageHeader, target)
            ?? throw new InvalidOperationException("The message is not reserved by this target.");
        offer.Release();
    }

    private FlowOutputOffer<T>? FindOffer(DataflowMessageHeader messageHeader, ITargetBlock<T> target)
    {
        if (!messageHeader.IsValid)
            throw new ArgumentException("A valid message header is required.", nameof(messageHeader));
        ArgumentNullException.ThrowIfNull(target);
        foreach (var link in Volatile.Read(ref _activeLinks))
        {
            var offer = Volatile.Read(ref link.Offer);
            if (offer?.Header.Id == messageHeader.Id && ReferenceEquals(link.Target, target))
                return offer;
        }
        return null;
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
        _dispatcher.Complete();
    }

    public void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        TransitionToFault(exception);
    }

    // Hosted signal disposal abandons queued delivery without faulting subscribers.
    internal void Abort()
    {
        FanoutLink[] links;
        lock (_gate)
        {
            links = _links;
            _links = [];
            if (_state is not (LifecycleState.Completed or LifecycleState.Faulted))
                _state = LifecycleState.Finalizing;
        }

        _acceptanceStopped.Cancel();
        foreach (var link in links)
            link.Dispose();
        _dispatcher.Complete();
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
            await _dispatchCompletion.ConfigureAwait(false);
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

    private Task DispatchAsync(object item)
    {
        if (item is DrainBarrier barrier)
        {
            barrier.Completion.TrySetResult();
            return Task.CompletedTask;
        }

        Delivery delivery;
        if (item is Delivery accepted)
        {
            _availableSlots.Release();
            delivery = accepted;
        }
        else
        {
            if (_prepareDispatch is { } prepare)
                return DispatchPreparedAsync((T)item, prepare);
            lock (_gate)
                delivery = new Delivery((T)item, _links, _prepareRouting);
        }

        return _prepareDispatch is { } prepared
            ? DispatchPreparedAsync(delivery.Value, prepared)
            : DeliverAsync(delivery);
    }

    private async Task DispatchPreparedAsync(T value,
        Func<T, CancellationToken, ValueTask<FlowOutputDispatch<T>>> prepare)
    {
        Volatile.Write(ref _activePreparedDispatch, 1);
        try
        {
            var dispatch = await prepare(value, _deliveryStopped.Token).ConfigureAwait(false);
            _deliveryStopped.Token.ThrowIfCancellationRequested();
            await DeliverRoutesAsync(dispatch.Routes, value).ConfigureAwait(false);
            _deliveryStopped.Token.ThrowIfCancellationRequested();
            dispatch.Delivered?.Invoke();
        }
        catch (Exception error)
        {
            TransitionToFault(Unwrap(error));
            throw;
        }
        finally
        {
            Volatile.Write(ref _activePreparedDispatch, 0);
        }
    }

    private async Task ObserveDispatchAsync()
    {
        try
        {
            await _dispatcher.Completion.ConfigureAwait(false);
            lock (_gate)
            {
                if (_state == LifecycleState.Faulted)
                {
                    throw _fault!;
                }
                _state = LifecycleState.Finalizing;
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
            CompleteFaultedDispatch();
        }
        catch (Exception exception)
        {
            TransitionToFault(Unwrap(exception));
            CompleteFaultedDispatch();
        }
    }

    private async Task DeliverAsync(Delivery delivery)
    {
        Volatile.Write(ref _activeLinks, delivery.Links);
        try
        {
            Exception? selectionFailure = null;
            try { delivery.PrepareRouting?.Invoke(delivery.Value); }
            catch (Exception error) { selectionFailure = error; }

            await DeliverRoutesAsync(delivery.Links, delivery.Value, selectionFailure).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            TransitionToFault(Unwrap(exception));
            throw;
        }
        finally
        {
            Volatile.Write(ref _activeLinks, []);
        }
    }

    private Task DeliverRoutesAsync(IFlowOutputRoute<T>[] routes, T value, Exception? selectionFailure = null)
    {
        if (selectionFailure is null && routes.Length <= 1)
            return routes.Length == 0 ? Task.CompletedTask : routes[0].DeliverAsync(value, _deliveryStopped.Token).AsTask();

        var deliveries = new Task[routes.Length];
        for (var index = 0; index < routes.Length; index++)
        {
            // Offer to every selected sibling before a failure cancels blocked delivery.
            try { deliveries[index] = routes[index].DeliverAsync(value, _deliveryStopped.Token).AsTask(); }
            catch (Exception error) { deliveries[index] = Task.FromException(error); }
        }

        if (selectionFailure is not null) ReportDeliveryFailure(selectionFailure);
        for (var index = 0; index < deliveries.Length; index++)
            deliveries[index] = ObserveDeliveryAsync(deliveries[index]);
        var completion = Task.WhenAll(deliveries);
        return selectionFailure is null ? completion : PreserveSelectionFailureAsync(completion, selectionFailure);
    }

    private static async Task PreserveSelectionFailureAsync(Task completion, Exception selectionFailure)
    {
        try { await completion.ConfigureAwait(false); }
        finally { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(selectionFailure).Throw(); }
    }

    private async Task ObserveDeliveryAsync(Task delivery)
    {
        try { await delivery.ConfigureAwait(false); }
        catch (Exception error)
        {
            ReportDeliveryFailure(Unwrap(error));
            throw;
        }
    }

    private void ReportDeliveryFailure(Exception error)
    {
        try { TransitionToFault(error); }
        catch { /* A throwing cancellation callback must not prevent joining started deliveries. */ }
    }

    private async Task DeliverToLinkAsync(FanoutLink link, T value)
    {
        if (link.IsDisposed)
        {
            return;
        }

        FlowOutputOffer<T>? offer = null;
        try
        {
            bool accepted;
            if (link.Route is { } route)
            {
                if (!route.Selected(value)) return;
                // Named graph routes need native target admission, not the public
                // source callback bridge. The native sender owns postponement.
                accepted = route.RejectWhenFull
                    ? link.Target.Post(value)
                    : await link.Target.SendAsync(value, link.CancellationToken).ConfigureAwait(false);
            }
            else
            {
                var header = Interlocked.Increment(ref _nextMessageHeader);
                if (header == 0)
                    header = Interlocked.Increment(ref _nextMessageHeader);
                offer = new FlowOutputOffer<T>(this, link.Target, new DataflowMessageHeader(header));
                Volatile.Write(ref link.Offer, offer);
                accepted = await offer.SendAsync(value, link.CancellationToken).ConfigureAwait(false);
            }
            if (!accepted)
            {
                if (link.IsDisposed || offer?.WasDeclined == true)
                    return;
                throw new InvalidOperationException(
                    link.Route?.RejectionMessage ?? "A reliable output target stopped accepting messages.");
            }

            link.MarkDelivered();
        }
        catch (OperationCanceledException) when (link.IsDisposed)
        {
        }
        catch (ObjectDisposedException) when (link.IsDisposed)
        {
        }
        catch (Exception exception) when (link.Route?.OnRejected is not null && !_deliveryStopped.IsCancellationRequested)
        {
            // An isolated graph admission failure does not remove the route: a full
            // target may accept the next message. The route owns safe reporting.
            link.Route.OnRejected(value, exception);
        }
        catch (Exception exception) when (_isolateTargetFailures && !_deliveryStopped.IsCancellationRequested)
        {
            DetachFailedLink(link, Unwrap(exception));
        }
        finally
        {
            if (offer is not null)
            {
                offer.Finish();
                Volatile.Write(ref link.Offer, null);
            }
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
            transitioned = true;
        }

        if (!transitioned)
        {
            return;
        }

        _acceptanceStopped.Cancel();
        _deliveryStopped.Cancel();
        ((IDataflowBlock)_dispatcher).Fault(exception);
        // A reserved target may need the graph fault before releasing its offer.
        // Notify now, rather than waiting for this output's completion observer.
        _routingFault?.Invoke(exception);
    }

    private void CompleteFaultedDispatch()
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
                try { link.Target.Complete(); }
                catch (Exception exception) when (_isolateTargetFailures)
                {
                    DetachFailedLink(link, exception);
                }
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
                catch (Exception failure)
                {
                    if (_isolateTargetFailures)
                        DetachFailedLink(link, failure);
                    // The reliable output fault remains the authoritative failure.
                }
            }
        }
    }

    private void DetachFailedLink(FanoutLink link, Exception exception)
    {
        try { link.Dispose(); }
        catch (Exception cleanupFailure) { exception = new AggregateException(exception, cleanupFailure); }
        try { _deliveryFailure?.Invoke(exception); }
        catch { /* A diagnostic observer cannot break sibling delivery. */ }
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

    private sealed record Delivery(T Value, FanoutLink[] Links, Action<T>? PrepareRouting);

    private sealed record RoutePolicy(Predicate<T> Selected, bool RejectWhenFull, string RejectionMessage,
        Action<T, Exception>? OnRejected);

    private sealed class DrainBarrier
    {
        internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class FanoutLink : IFlowOutputRoute<T>, IDisposable
    {
        private readonly FlowOutput<T> _owner;
        public FlowOutputOffer<T>? Offer;
        public RoutePolicy? Route { get; set; }
        private readonly CancellationTokenSource _stopped;
        private readonly Action<FanoutLink> _remove;
        private long _remainingMessages;
        private int _disposed;

        public FanoutLink(
            FlowOutput<T> owner,
            ITargetBlock<T> target,
            bool propagateCompletion,
            long maxMessages,
            CancellationToken outputStopped,
            Action<FanoutLink> remove)
        {
            _owner = owner;
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

        public ValueTask DeliverAsync(T value, CancellationToken cancellationToken)
            => new(_owner.DeliverToLinkAsync(this, value));

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
