using FluxFlow.Composition.Addressing;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.Ports;

internal sealed record ApplicationInputMessageIdentity(
    CorrelationId CorrelationId,
    TraceId TraceId,
    MessageId MessageId);

internal sealed class ApplicationInputPortCore<TMessage, TTarget>
    where TMessage : class
    where TTarget : class
{
    private readonly object _gate = new();
    private readonly Queue<TMessage> _queue = new();
    private readonly SemaphoreSlim _availableCapacity;
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly SemaphoreSlim _attachmentGate = new(1, 1);
    private readonly CancellationTokenSource _abort = new();
    private readonly Action<ApplicationPortRejection> _report;
    private readonly Action<ApplicationPortActivity> _activity;
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _pump;
    private readonly string _role;
    private readonly string _targetRequirement;
    private readonly Func<TMessage, ApplicationInputMessageIdentity> _getIdentity;
    private readonly Func<TMessage, TTarget, CancellationToken, ValueTask<bool>> _dispatch;
    private readonly Func<TTarget, Task> _getTargetCompletion;
    private readonly Func<TTarget, Task> _completeTarget;

    private TTarget? _target;
    private TMessage? _retry;
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
        Action<ApplicationPortActivity> activity,
        Func<TMessage, ApplicationInputMessageIdentity> getIdentity,
        Func<TMessage, TTarget, CancellationToken, ValueTask<bool>> dispatch,
        Func<TTarget, Task> getTargetCompletion,
        Func<TTarget, Task> completeTarget)
    {
        AddressCore = address;
        KindCore = kind;
        PayloadTypeCore = payloadType;
        CapacityCore = capacity;
        _role = role;
        _targetRequirement = targetRequirement;
        _report = report;
        _activity = activity;
        _getIdentity = getIdentity;
        _dispatch = dispatch;
        _getTargetCompletion = getTargetCompletion;
        _completeTarget = completeTarget;
        _availableCapacity = new SemaphoreSlim(capacity, capacity);
        _pump = PumpAsync();
    }

    internal Task Completion => _completion.Task;

    private ApplicationAddress AddressCore { get; }

    private Type PayloadTypeCore { get; }

    private ApplicationPortKind KindCore { get; }

    private int CapacityCore { get; }

    internal PortSendResult TrySend(
        TMessage message,
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
            else if (!_availableCapacity.Wait(0))
            {
                status = PortSendStatus.Full;
            }
            else
            {
                _queue.Enqueue(message);
                NotifyStateChanged();
                Pulse();
                status = PortSendStatus.Accepted;
            }
        }

        var identity = _getIdentity(message);
        if (status == PortSendStatus.Accepted)
        {
            _activity(new ApplicationPortActivity(
                DateTimeOffset.UtcNow,
                ApplicationPortActivityKind.InputAccepted,
                AddressCore,
                source,
                identity.CorrelationId,
                identity.TraceId,
                identity.MessageId));
        }
        else
        {
            Report(status, identity, source);
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
                PendingMessages = _aborted ? 0 : CapacityCore - _availableCapacity.CurrentCount,
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
            Pulse();
        }
    }

    internal void Abort()
    {
        lock (_gate)
        {
            if (_aborted)
                return;

            _aborted = true;
            _completeRequested = true;
            _queue.Clear();
            _retry = default;
            _target = null;
            _inflightIdle.TrySetResult();
            NotifyStateChanged();
            _completion.TrySetResult();
        }

        _abort.Cancel();
        Pulse();
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
                if (_queue.Count == 0 &&
                    _retry is null &&
                    _inflightIdle.Task.IsCompleted)
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
                Pulse();
            }
        }
        finally
        {
            _attachmentGate.Release();
        }
    }

    internal long CommitRevision(object? target)
    {
        if (target is not null && target is not TTarget)
        {
            throw new InvalidOperationException(
                $"{_role} '{AddressCore}' requires {_targetRequirement}.");
        }

        var typedTarget = (TTarget?)target;
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
            Pulse();
        }

        _attachmentGate.Release();
    }

    private async Task PumpAsync()
    {
        try
        {
            while (true)
            {
                await _signal.WaitAsync(_abort.Token).ConfigureAwait(false);

                while (true)
                {
                    TMessage? message = default;
                    TTarget? target = null;
                    List<TMessage>? terminalDrops = null;
                    var finish = false;

                    lock (_gate)
                    {
                        if (_aborted)
                            return;
                        if (_paused)
                            break;

                        if (_target is null)
                        {
                            if (_completeRequested)
                            {
                                terminalDrops = [];
                                if (_retry is not null)
                                {
                                    terminalDrops.Add(_retry);
                                    _retry = default;
                                }

                                while (_queue.TryDequeue(out var queued))
                                    terminalDrops.Add(queued);
                                NotifyStateChanged();
                            }
                            else
                            {
                                break;
                            }
                        }
                        else
                        {
                            message = _retry;
                            if (message is not null)
                                _retry = default;
                            else if (_queue.Count > 0)
                                message = _queue.Dequeue();

                            if (message is null)
                            {
                                if (_completeRequested)
                                {
                                    target = _target;
                                    _target = null;
                                    NotifyStateChanged();
                                    finish = true;
                                }
                                else
                                {
                                    break;
                                }
                            }
                            else
                            {
                                target = _target;
                                _inflightIdle = new TaskCompletionSource(
                                    TaskCreationOptions.RunContinuationsAsynchronously);
                            }
                        }
                    }

                    if (terminalDrops is not null)
                    {
                        if (terminalDrops.Count > 0)
                            _availableCapacity.Release(terminalDrops.Count);
                        foreach (var dropped in terminalDrops)
                            Report(PortSendStatus.Completed, _getIdentity(dropped), source: null);
                        _completion.TrySetResult();
                        return;
                    }

                    if (finish)
                    {
                        try
                        {
                            await _completeTarget(target!).ConfigureAwait(false);
                            _completion.TrySetResult();
                        }
                        catch (Exception exception)
                        {
                            _completion.TrySetException(exception.GetBaseException());
                        }
                        return;
                    }

                    var accepted = false;
                    Exception? failure = null;
                    try
                    {
                        accepted = await _dispatch(message!, target!, _abort.Token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (_abort.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                    }

                    lock (_gate)
                    {
                        if (accepted)
                        {
                            _availableCapacity.Release();
                        }
                        else if (!_aborted)
                        {
                            _retry = message;
                            if (ReferenceEquals(_target, target))
                                _target = null;
                        }

                        _inflightIdle.TrySetResult();
                        NotifyStateChanged();
                        Pulse();
                    }

                    if (!accepted && !_aborted)
                    {
                        var identity = _getIdentity(message!);
                        _report(new ApplicationPortRejection
                        {
                            Timestamp = DateTimeOffset.UtcNow,
                            Port = AddressCore,
                            CorrelationId = identity.CorrelationId,
                            TraceId = identity.TraceId,
                            MessageId = identity.MessageId,
                            Reason = ApplicationPortRejectionReason.TargetRejected,
                            Exception = failure
                        });
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_abort.IsCancellationRequested)
        {
            _completion.TrySetResult();
        }
        catch (Exception exception)
        {
            _completion.TrySetException(exception);
        }
    }

    private void ObserveTargetCompletion(TTarget target, long generation)
    {
        _ = _getTargetCompletion(target).ContinueWith(
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
        TTarget target,
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
            Pulse();
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
        ApplicationInputMessageIdentity identity,
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
            CorrelationId = identity.CorrelationId,
            TraceId = identity.TraceId,
            MessageId = identity.MessageId,
            Reason = reason
        });
    }

    private void Pulse()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
        }
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
        ApplicationInputPortCore<TMessage, TTarget> Owner,
        TTarget Target,
        long Generation);
}
