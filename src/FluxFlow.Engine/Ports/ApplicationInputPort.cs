using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition.Addressing;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.Ports;

internal interface IApplicationInputPort
{
    ApplicationAddress Address { get; }

    Type PayloadType { get; }

    Task Completion { get; }

    void Complete();

    void Abort();
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
        Action<ApplicationPortRejection> report)
    {
        Address = address;
        Capacity = capacity;
        _report = report;
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
                return CreateSendResult(PortSendStatus.Accepted);
            }
        }

        Report(status, message, source);
        return CreateSendResult(status);
    }

    public async ValueTask<IAsyncDisposable> AttachAsync(
        ITargetBlock<FlowMessage<T>> target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
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

            try
            {
                await waitForIdle.WaitAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch
            {
                lock (_gate)
                {
                    _paused = false;
                    Pulse();
                }

                throw;
            }

            long generation;
            lock (_gate)
            {
                if (_aborted)
                {
                    _paused = false;
                    Pulse();
                    throw new ObjectDisposedException(GetType().FullName);
                }

                if (_completeRequested)
                {
                    _paused = false;
                    Pulse();
                    throw new InvalidOperationException($"Input port '{Address}' is completed.");
                }

                _target = target;
                generation = ++_generation;
                _paused = false;
                Pulse();
            }

            return new InputAttachment(this, generation);
        }
        finally
        {
            _attachmentGate.Release();
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

                                _completion.TrySetResult();
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
                        foreach (var dropped in terminalDrops)
                            Report(PortSendStatus.Completed, dropped, source: null);
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
}
