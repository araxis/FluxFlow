using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Addressing;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.Ports;

internal sealed class ApplicationInputPortCore
{
    private readonly object _gate = new();
    private readonly ActionBlock<FlowMessage> _dispatcher;
    private readonly SemaphoreSlim _attachmentGate = new(1, 1);
    private readonly CancellationTokenSource _abort = new();
    private readonly CancellationTokenSource _admissionStopped = new();
    private readonly Action<ApplicationPortRejection> _report;
    private readonly Action<ApplicationPortActivity> _activity;
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _role;
    private readonly string _targetRequirement;

    private ITargetBlock<FlowMessage>? _target;
    // Drain owns both accepted block work and native admissions already in progress.
    // Waiting callers are not additional items in the bounded input queue.
    private int _outstanding;
    private int _activeDispatch;
    private TaskCompletionSource _inflightIdle = CompletedSignal();
    private TaskCompletionSource _stateChanged = PendingSignal();
    private long _generation;
    private bool _paused;
    private bool _draining;
    private bool _completeRequested;
    private bool _aborted;

    internal ApplicationInputPortCore(
        ApplicationAddress address,
        ApplicationPortKind kind,
        Type payloadType,
        int capacity,
        string role,
        string targetRequirement,
        Action<ApplicationPortRejection> report,
        Action<ApplicationPortActivity> activity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        AddressCore = address;
        KindCore = kind;
        PayloadTypeCore = payloadType;
        CapacityCore = capacity;
        _role = role;
        _targetRequirement = targetRequirement;
        _report = report;
        _activity = activity;
        _dispatcher = new ActionBlock<FlowMessage>(DispatchAsync, new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = capacity,
            MaxDegreeOfParallelism = 1,
            CancellationToken = _abort.Token
        });
        _ = ObserveDispatchAsync();
    }

    internal Task Completion => _completion.Task;

    private ApplicationAddress AddressCore { get; }

    private Type PayloadTypeCore { get; }

    private ApplicationPortKind KindCore { get; }

    private int CapacityCore { get; }

    internal PortSendResult TrySend(
        FlowMessage message,
        ApplicationAddress? source)
    {
        PortSendStatus status;
        lock (_gate)
        {
            if (_completeRequested || _aborted || _draining)
            {
                status = PortSendStatus.Completed;
            }
            else if (_target is null)
            {
                status = PortSendStatus.Unavailable;
            }
            else if (!_dispatcher.Post(message))
            {
                status = PortSendStatus.Full;
            }
            else
            {
                _outstanding++;
                NotifyStateChanged();
                status = PortSendStatus.Accepted;
            }
        }

        return ReportAdmission(message, source, status);
    }

    internal async ValueTask<PortSendResult> SendAsync(
        FlowMessage message, ApplicationAddress? source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var admissionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _admissionStopped.Token);
        Task<bool>? admission = null;
        PortSendStatus status;
        lock (_gate)
        {
            status = _completeRequested || _aborted || _draining ? PortSendStatus.Completed
                : _target is null ? PortSendStatus.Unavailable : PortSendStatus.Accepted;
            if (status == PortSendStatus.Accepted)
            {
                _outstanding++;
                try { admission = _dispatcher.SendAsync(message, admissionCancellation.Token); }
                catch { _outstanding--; NotifyStateChanged(); throw; }
            }
        }

        if (admission is not null)
        {
            var accepted = false;
            try
            {
                accepted = await admission.ConfigureAwait(false);
                status = accepted ? PortSendStatus.Accepted : PortSendStatus.Completed;
            }
            catch (OperationCanceledException) when (_admissionStopped.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                status = PortSendStatus.Completed;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            finally
            {
                if (!accepted)
                {
                    lock (_gate)
                    {
                        if (!_aborted) _outstanding--;
                        NotifyStateChanged();
                    }
                }
            }
        }
        return ReportAdmission(message, source, status);
    }

    private PortSendResult ReportAdmission(FlowMessage message, ApplicationAddress? source, PortSendStatus status)
    {
        if (status == PortSendStatus.Accepted)
        {
            _activity(new ApplicationPortActivity(
                DateTimeOffset.UtcNow,
                ApplicationPortActivityKind.InputAccepted,
                AddressCore,
                source,
                message.CorrelationId,
                message.TraceId,
                message.MessageId));
        }
        else
        {
            Report(status, message, source);
        }

        return new PortSendResult
        {
            Port = AddressCore,
            Status = status
        };
    }

    internal ApplicationPortStatus GetStatus()
    {
        lock (_gate)
        {
            return new ApplicationPortStatus
            {
                Address = AddressCore,
                Direction = ApplicationPortDirection.Input,
                Kind = KindCore,
                PayloadType = PayloadTypeCore,
                Capacity = CapacityCore,
                PendingMessages = _aborted ? 0 : _dispatcher.InputCount + _activeDispatch,
                ActiveAttachments = _target is null ? 0 : 1,
                Availability = _completeRequested || _aborted || _draining
                    ? ApplicationPortAvailability.Completed
                    : _target is null
                        ? ApplicationPortAvailability.Unavailable
                        : ApplicationPortAvailability.Available
            };
        }
    }

    internal async ValueTask BeginRevisionAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _attachmentGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Task waitForIdle;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_aborted, this);
                if (_completeRequested)
                    throw new InvalidOperationException($"{_role} '{AddressCore}' is completed.");

                _paused = true;
                NotifyStateChanged();
                waitForIdle = _inflightIdle.Task;
            }

            await waitForIdle.WaitAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_aborted, this);
                if (_completeRequested)
                    throw new InvalidOperationException($"{_role} '{AddressCore}' is completed.");
            }
        }
        catch
        {
            EndRevision();
            throw;
        }
    }

    internal void Complete()
    {
        lock (_gate)
        {
            if (_completeRequested || _aborted)
                return;

            _completeRequested = true;
            NotifyStateChanged();
            _dispatcher.Complete();
        }
        // Stop waiting producers without canceling work already accepted by the block.
        _admissionStopped.Cancel();
    }

    internal void Abort()
    {
        lock (_gate)
        {
            if (_aborted)
                return;

            _aborted = true;
            _completeRequested = true;
            _outstanding = 0;
            _target = null;
            _inflightIdle.TrySetResult();
            NotifyStateChanged();
            _completion.TrySetResult();
        }

        _admissionStopped.Cancel();
        _abort.Cancel();
    }

    internal async ValueTask DrainAsync(
        long generation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (true)
        {
            Task stateChanged;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_aborted, this);
                if (generation != _generation)
                    return;

                _draining = true;
                if (_outstanding == 0)
                {
                    return;
                }

                if (_target is null)
                {
                    throw new InvalidOperationException(
                        $"{_role} '{AddressCore}' cannot drain without an active target.");
                }

                stateChanged = _stateChanged.Task;
            }

            await stateChanged.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    internal async ValueTask DetachAsync(long generation)
    {
        await _attachmentGate.WaitAsync().ConfigureAwait(false);
        try
        {
            Task waitForIdle;
            lock (_gate)
            {
                if (_aborted || generation != _generation || _target is null)
                    return;

                _paused = true;
                waitForIdle = _inflightIdle.Task;
            }

            await waitForIdle.ConfigureAwait(false);

            lock (_gate)
            {
                if (generation == _generation)
                    _target = null;

                _paused = false;
                NotifyStateChanged();
            }
        }
        finally
        {
            _attachmentGate.Release();
        }
    }

    internal long CommitRevision(object? target)
    {
        if (target is not null && target is not ITargetBlock<FlowMessage>)
        {
            throw new InvalidOperationException(
                $"{_role} '{AddressCore}' requires {_targetRequirement}.");
        }

        var typedTarget = (ITargetBlock<FlowMessage>?)target;
        long generation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_aborted, this);
            if (_completeRequested)
                throw new InvalidOperationException($"{_role} '{AddressCore}' is completed.");

            _target = typedTarget;
            _draining = false;
            generation = ++_generation;
            NotifyStateChanged();
        }

        if (typedTarget is not null)
            ObserveTargetCompletion(typedTarget, generation);
        return generation;
    }

    internal void EndRevision()
    {
        lock (_gate)
        {
            _paused = false;
            NotifyStateChanged();
        }

        _attachmentGate.Release();
    }

    private async Task DispatchAsync(FlowMessage message)
    {
        lock (_gate) _activeDispatch = 1;
        try
        {
            // The block retains this item ahead of its queue while an attachment
            // is paused or replaced. No second retry queue or scheduling signal.
            while (true)
            {
                ITargetBlock<FlowMessage>? target;
                Task? stateChanged = null;
                lock (_gate)
                {
                    if (_aborted) return;
                    target = _paused ? null : _target;
                    if (target is null)
                    {
                        if (_paused || !_completeRequested)
                            stateChanged = _stateChanged.Task;
                    }
                    else
                    {
                        _inflightIdle = PendingSignal();
                    }
                }

                if (stateChanged is not null)
                {
                    await stateChanged.WaitAsync(_abort.Token).ConfigureAwait(false);
                    continue;
                }
                if (target is null)
                {
                    Report(PortSendStatus.Completed, message, source: null);
                    return;
                }

                bool accepted = false;
                Exception? failure = null;
                try
                {
                    accepted = await target.SendAsync(message, _abort.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_abort.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception) { failure = exception; }

                lock (_gate)
                {
                    if (!accepted && !_aborted && ReferenceEquals(_target, target))
                        _target = null;
                    _inflightIdle.TrySetResult();
                    if (_aborted || accepted) return;
                    NotifyStateChanged();
                }

                _report(new ApplicationPortRejection
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    Port = AddressCore,
                    CorrelationId = message.CorrelationId,
                    TraceId = message.TraceId,
                    MessageId = message.MessageId,
                    Reason = ApplicationPortRejectionReason.TargetRejected,
                    Exception = failure
                });
            }
        }
        finally
        {
            lock (_gate)
            {
                _activeDispatch = 0;
                if (!_aborted) _outstanding--;
                NotifyStateChanged();
            }
        }
    }

    private async Task ObserveDispatchAsync()
    {
        try
        {
            await _dispatcher.Completion.ConfigureAwait(false);
            ITargetBlock<FlowMessage>? target;
            while (true)
            {
                Task stateChanged;
                lock (_gate)
                {
                    if (_aborted) return;
                    if (!_paused)
                    {
                        target = _target;
                        _target = null;
                        NotifyStateChanged();
                        break;
                    }
                    stateChanged = _stateChanged.Task;
                }
                await stateChanged.WaitAsync(_abort.Token).ConfigureAwait(false);
            }

            // Terminal controls stay open until their component closes them.
            if (target is not null && KindCore == ApplicationPortKind.Message)
            {
                target.Complete();
                await target.Completion.ConfigureAwait(false);
            }
            _completion.TrySetResult();
        }
        catch (OperationCanceledException) when (_abort.IsCancellationRequested)
        {
            _completion.TrySetResult();
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                _completeRequested = true;
                _target = null;
                _inflightIdle.TrySetResult();
                NotifyStateChanged();
            }
            _completion.TrySetException(exception.GetBaseException());
        }
    }

    private void ObserveTargetCompletion(ITargetBlock<FlowMessage> target, long generation)
    {
        _ = target.Completion.ContinueWith(
            static (task, state) =>
            {
                var completion = (TargetCompletion)state!;
                completion.Owner.HandleTargetCompletion(
                    completion.Target,
                    completion.Generation,
                    task);
            },
            new TargetCompletion(this, target, generation),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void HandleTargetCompletion(
        ITargetBlock<FlowMessage> target,
        long generation,
        Task completion)
    {
        Exception? failure = null;
        lock (_gate)
        {
            if (_aborted ||
                generation != _generation ||
                !ReferenceEquals(_target, target))
            {
                return;
            }

            _target = null;
            failure = completion.IsFaulted
                ? completion.Exception?.GetBaseException() ?? completion.Exception
                : null;
            NotifyStateChanged();
        }

        if (failure is not null)
        {
            _report(new ApplicationPortRejection
            {
                Timestamp = DateTimeOffset.UtcNow,
                Port = AddressCore,
                Reason = ApplicationPortRejectionReason.ComponentFaulted,
                Exception = failure
            });
        }
    }

    private void Report(
        PortSendStatus status,
        FlowMessage message,
        ApplicationAddress? source)
    {
        var reason = status switch
        {
            PortSendStatus.Full => ApplicationPortRejectionReason.Full,
            PortSendStatus.Unavailable => ApplicationPortRejectionReason.Unavailable,
            PortSendStatus.Completed => ApplicationPortRejectionReason.Completed,
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };

        _report(new ApplicationPortRejection
        {
            Timestamp = DateTimeOffset.UtcNow,
            Port = AddressCore,
            RelatedPort = source,
            CorrelationId = message.CorrelationId,
            TraceId = message.TraceId,
            MessageId = message.MessageId,
            Reason = reason
        });
    }

    private void NotifyStateChanged()
    {
        var previous = _stateChanged;
        _stateChanged = PendingSignal();
        previous.TrySetResult();
    }

    private static TaskCompletionSource CompletedSignal()
    {
        var source = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }

    private static TaskCompletionSource PendingSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed record TargetCompletion(
        ApplicationInputPortCore Owner,
        ITargetBlock<FlowMessage> Target,
        long Generation);
}
