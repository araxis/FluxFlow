using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition.Addressing;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.Ports;

internal interface IApplicationInputPort
{
    ApplicationAddress Address { get; }

    Type PayloadType { get; }

    Task Completion { get; }

    ApplicationPortStatus GetStatus();

    ValueTask<IApplicationInputRevision> BeginRevisionAsync(CancellationToken cancellationToken);

    void Complete();

    void Abort();
}

internal interface IApplicationInputRevision : IAsyncDisposable
{
    ApplicationAddress Address { get; }

    Type PayloadType { get; }

    IAsyncDisposable Commit(object? target);
}

internal sealed class ApplicationInputPort<T> : IApplicationInputPort
{
    private readonly object _gate = new();
    private readonly Queue<FlowMessage<T>> _queue = new();
    private readonly SemaphoreSlim _availableCapacity;
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly SemaphoreSlim _attachmentGate = new(1, 1);
    private readonly CancellationTokenSource _abort = new();
    private readonly Action<ApplicationPortRejection> _report;
    private readonly Action<ApplicationPortActivity> _activity;
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _pump;

    private ITargetBlock<FlowMessage<T>>? _target;
    private FlowMessage<T>? _retry;
    private TaskCompletionSource _inflightIdle = CompletedSignal();
    private long _generation;
    private bool _paused;
    private bool _completeRequested;
    private bool _aborted;

    public ApplicationInputPort(
        ApplicationAddress address,
        int capacity,
        Action<ApplicationPortRejection> report,
        Action<ApplicationPortActivity> activity)
    {
        Address = address;
        Capacity = capacity;
        _report = report;
        _activity = activity;
        _availableCapacity = new SemaphoreSlim(capacity, capacity);
        _pump = PumpAsync();
    }

    public ApplicationAddress Address { get; }

    public Type PayloadType => typeof(T);

    public int Capacity { get; }

    public Task Completion => _completion.Task;

    public PortSendResult TrySend(
        FlowMessage<T> message,
        ApplicationAddress? source = null)
    {
        ArgumentNullException.ThrowIfNull(message);

        PortSendStatus status;
        lock (_gate)
        {
            if (_completeRequested || _aborted)
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
                Pulse();
                status = PortSendStatus.Accepted;
            }
        }

        if (status == PortSendStatus.Accepted)
        {
            _activity(new ApplicationPortActivity(
                DateTimeOffset.UtcNow,
                ApplicationPortActivityKind.InputAccepted,
                Address,
                source,
                message.CorrelationId,
                message.TraceId,
                message.MessageId));
        }
        else
        {
            Report(status, message, source);
        }

        return CreateSendResult(status);
    }

    public ApplicationPortStatus GetStatus()
    {
        lock (_gate)
        {
            return new ApplicationPortStatus
            {
                Address = Address,
                Direction = ApplicationPortDirection.Input,
                PayloadType = typeof(T),
                Capacity = Capacity,
                PendingMessages = _aborted ? 0 : Capacity - _availableCapacity.CurrentCount,
                ActiveAttachments = _target is null ? 0 : 1,
                Availability = _completeRequested || _aborted
                    ? ApplicationPortAvailability.Completed
                    : _target is null
                        ? ApplicationPortAvailability.Unavailable
                        : ApplicationPortAvailability.Available
            };
        }
    }

    public async ValueTask<IAsyncDisposable> AttachAsync(
        ITargetBlock<FlowMessage<T>> target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        var revision = await BeginRevisionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return revision.Commit(target);
        }
        finally
        {
            await revision.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask<IApplicationInputRevision> BeginRevisionAsync(
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
                    throw new InvalidOperationException($"Input port '{Address}' is completed.");

                _paused = true;
                waitForIdle = _inflightIdle.Task;
            }

            await waitForIdle.WaitAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_aborted, this);
                if (_completeRequested)
                    throw new InvalidOperationException($"Input port '{Address}' is completed.");
            }

            return new InputRevision(this);
        }
        catch
        {
            EndRevision();
            throw;
        }
    }

    public void Complete()
    {
        lock (_gate)
        {
            if (_completeRequested || _aborted)
                return;

            _completeRequested = true;
            Pulse();
        }
    }

    public void Abort()
    {
        lock (_gate)
        {
            if (_aborted)
                return;

            _aborted = true;
            _completeRequested = true;
            _queue.Clear();
            _retry = null;
            _target = null;
            _inflightIdle.TrySetResult();
            _completion.TrySetResult();
        }

        _abort.Cancel();
        Pulse();
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
                    FlowMessage<T>? message = null;
                    ITargetBlock<FlowMessage<T>>? target = null;
                    var finish = false;
                    List<FlowMessage<T>>? terminalDrops = null;

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
                                    _retry = null;
                                }

                                while (_queue.TryDequeue(out var queued))
                                    terminalDrops.Add(queued);

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
                                _retry = null;
                            else if (_queue.Count > 0)
                                message = _queue.Dequeue();

                            if (message is null)
                            {
                                if (_completeRequested)
                                {
                                    target = _target;
                                    _target = null;
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
                            Report(PortSendStatus.Completed, dropped, source: null);
                        _completion.TrySetResult();
                        return;
                    }

                    if (finish)
                    {
                        target!.Complete();
                        await CompleteFromTargetAsync(target).ConfigureAwait(false);
                        return;
                    }

                    var accepted = false;
                    Exception? failure = null;
                    try
                    {
                        accepted = await target!.SendAsync(message!, _abort.Token).ConfigureAwait(false);
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
                        Pulse();
                    }

                    if (!accepted && !_aborted)
                    {
                        _report(new ApplicationPortRejection
                        {
                            Timestamp = DateTimeOffset.UtcNow,
                            Port = Address,
                            TraceId = message!.TraceId,
                            MessageId = message.MessageId,
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

    private async Task CompleteFromTargetAsync(ITargetBlock<FlowMessage<T>> target)
    {
        try
        {
            await target.Completion.ConfigureAwait(false);
            _completion.TrySetResult();
        }
        catch (Exception exception)
        {
            _completion.TrySetException(exception.GetBaseException());
        }
    }

    private async ValueTask DetachAsync(long generation)
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
                Pulse();
            }
        }
        finally
        {
            _attachmentGate.Release();
        }
    }

    private IAsyncDisposable CommitRevision(object? target)
    {
        if (target is not null && target is not ITargetBlock<FlowMessage<T>>)
        {
            throw new InvalidOperationException(
                $"Input port '{Address}' requires target payload type '{typeof(T)}'.");
        }

        var typedTarget = (ITargetBlock<FlowMessage<T>>?)target;
        long generation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_aborted, this);
            if (_completeRequested)
                throw new InvalidOperationException($"Input port '{Address}' is completed.");

            _target = typedTarget;
            generation = ++_generation;
        }

        if (typedTarget is not null)
            ObserveTargetCompletion(typedTarget, generation);
        return new InputAttachment(this, generation);
    }

    private void ObserveTargetCompletion(
        ITargetBlock<FlowMessage<T>> target,
        long generation)
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

    private void EndRevision()
    {
        lock (_gate)
        {
            _paused = false;
            Pulse();
        }

        _attachmentGate.Release();
    }

    private void HandleTargetCompletion(
        ITargetBlock<FlowMessage<T>> target,
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
            Pulse();
        }

        if (failure is not null)
        {
            _report(new ApplicationPortRejection
            {
                Timestamp = DateTimeOffset.UtcNow,
                Port = Address,
                Reason = ApplicationPortRejectionReason.ComponentFaulted,
                Exception = failure
            });
        }
    }

    private void Report(
        PortSendStatus status,
        FlowMessage<T> message,
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
            Port = Address,
            RelatedPort = source,
            CorrelationId = message.CorrelationId,
            TraceId = message.TraceId,
            MessageId = message.MessageId,
            Reason = reason
        });
    }

    private PortSendResult CreateSendResult(PortSendStatus status)
        => new()
        {
            Port = Address,
            Status = status
        };

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

    private static TaskCompletionSource CompletedSignal()
    {
        var source = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }

    private sealed class InputAttachment(
        ApplicationInputPort<T> owner,
        long generation) : IAsyncDisposable
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                await owner.DetachAsync(generation).ConfigureAwait(false);
        }
    }

    private sealed class InputRevision(ApplicationInputPort<T> owner) :
        IApplicationInputRevision
    {
        private int _committed;
        private int _disposed;

        public ApplicationAddress Address => owner.Address;

        public Type PayloadType => owner.PayloadType;

        public IAsyncDisposable Commit(object? target)
        {
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(InputRevision));
            if (Interlocked.Exchange(ref _committed, 1) != 0)
                throw new InvalidOperationException($"Input port '{Address}' revision was already committed.");
            return owner.CommitRevision(target);
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.EndRevision();
            return ValueTask.CompletedTask;
        }
    }

    private sealed record TargetCompletion(
        ApplicationInputPort<T> Owner,
        ITargetBlock<FlowMessage<T>> Target,
        long Generation);
}
