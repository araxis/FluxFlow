using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Links;
using FluxFlow.Mapping;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.Ports;

internal interface IApplicationOutputPort
{
    ApplicationAddress Address { get; }

    Type PayloadType { get; }

    Task Completion { get; }

    ApplicationPortStatus GetStatus();

    ValueTask<IApplicationOutputRevision> BeginRevisionAsync(CancellationToken cancellationToken);

    IPreparedApplicationOutput PrepareRevisionOutput(object source);

    IDisposable Connect(IApplicationInputPort input, CompiledApplicationLink link);

    IApplicationRevisionRoute CreateRevisionRoute(
        IApplicationInputPort input,
        CompiledApplicationLink link);

    void Complete();

    void Abort();
}

internal interface IApplicationOutputRevision : IAsyncDisposable
{
    ApplicationAddress Address { get; }
}

internal interface IPreparedApplicationOutput : IDisposable
{
    ApplicationAddress Address { get; }

    void ThrowIfFaulted();

    void Activate();

    ValueTask DrainAsync(CancellationToken cancellationToken);
}

internal sealed class ApplicationOutputPort<T> : IApplicationOutputPort
{
    private readonly object _gate = new();
    private readonly BufferBlock<FlowMessage<T>> _ingress;
    private readonly Action<ApplicationPortRejection> _report;
    private readonly Action<ApplicationPortActivity> _activity;
    private readonly ApplicationRevisionRouting _revisionRouting;
    private readonly List<IApplicationOutputLink<T>> _links = [];
    private readonly List<ApplicationOutputReceiveWaiter<T>> _waiters = [];
    private readonly List<PortObservation<T>> _observations = [];
    private readonly List<ApplicationOutputAttachment<T>> _attachments = [];
    private readonly CancellationTokenSource _abort = new();
    private readonly SemaphoreSlim _dispatchGate = new(1, 1);
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _pump;

    private bool _completeRequested;
    private bool _aborted;
    private int _activeSources;

    public ApplicationOutputPort(
        ApplicationAddress address,
        int capacity,
        Action<ApplicationPortRejection> report,
        Action<ApplicationPortActivity> activity,
        ApplicationRevisionRouting revisionRouting)
    {
        Address = address;
        Capacity = capacity;
        _report = report;
        _activity = activity;
        _revisionRouting = revisionRouting;
        _ingress = new BufferBlock<FlowMessage<T>>(new DataflowBlockOptions
        {
            BoundedCapacity = capacity
        });
        _pump = PumpAsync();
    }

    public ApplicationAddress Address { get; }

    public Type PayloadType => typeof(T);

    public int Capacity { get; }

    public Task Completion => _completion.Task;

    public ApplicationPortStatus GetStatus()
    {
        lock (_gate)
        {
            return new ApplicationPortStatus
            {
                Address = Address,
                Direction = ApplicationPortDirection.Output,
                Kind = ApplicationPortKind.Message,
                PayloadType = typeof(T),
                Capacity = Capacity,
                PendingMessages = _aborted ? 0 : _ingress.Count,
                ActiveAttachments = _activeSources,
                Availability = _completeRequested || _aborted
                    ? ApplicationPortAvailability.Completed
                    : _activeSources == 0
                        ? ApplicationPortAvailability.Unavailable
                        : ApplicationPortAvailability.Available
            };
        }
    }

    public async ValueTask<IApplicationOutputRevision> BeginRevisionAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _dispatchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            if (_aborted)
            {
                _dispatchGate.Release();
                throw new ObjectDisposedException(GetType().FullName);
            }

            if (_completeRequested)
            {
                _dispatchGate.Release();
                throw new InvalidOperationException($"Output port '{Address}' is completed.");
            }
        }

        return new ApplicationOutputRevision<T>(this);
    }

    public IPreparedApplicationOutput PrepareRevisionOutput(object source)
    {
        if (source is not ISourceBlock<FlowMessage<T>> typedSource)
        {
            throw new InvalidOperationException(
                $"Output port '{Address}' requires source payload type '{typeof(T)}'.");
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_aborted, this);
            if (_completeRequested)
                throw new InvalidOperationException($"Output port '{Address}' is completed.");
        }

        return new PreparedApplicationOutput<T>(this, typedSource);
    }

    public IDisposable Attach(ISourceBlock<FlowMessage<T>> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        ApplicationOutputAttachment<T> attachment;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_aborted, this);
            if (_completeRequested)
                throw new InvalidOperationException($"Output port '{Address}' is completed.");

            var link = source.LinkTo(
                _ingress,
                new DataflowLinkOptions { PropagateCompletion = false });
            attachment = new ApplicationOutputAttachment<T>(this, link);
            _attachments.Add(attachment);
            _activeSources++;
        }

        _ = source.Completion.ContinueWith(
            static (task, state) =>
            {
                var value = (ApplicationOutputAttachment<T>)state!;
                if (task.IsFaulted)
                    value.ReportSourceFault(task.Exception?.GetBaseException() ?? task.Exception!);
                value.Dispose();
            },
            attachment,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return attachment;
    }

    public IDisposable Connect(
        IApplicationInputPort input,
        CompiledApplicationLink link)
    {
        if (input is IApplicationSignalInputPort signalInput)
        {
            var signalLink = new ApplicationSignalOutputLink<T>(this, signalInput, link);
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_aborted, this);
                if (_completeRequested)
                    throw new InvalidOperationException($"Output port '{Address}' is completed.");
                _links.Add(signalLink);
            }

            return signalLink;
        }

        if (input is not ApplicationInputPort<T> typedInput)
        {
            throw new InvalidOperationException(
                $"Link '{link.Source}' to '{link.Target}' requires exact payload type '{typeof(T)}'.");
        }

        var outputLink = new ApplicationMessageOutputLink<T>(this, typedInput, link);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_aborted, this);
            if (_completeRequested)
                throw new InvalidOperationException($"Output port '{Address}' is completed.");
            _links.Add(outputLink);
        }

        return outputLink;
    }

    public IApplicationRevisionRoute CreateRevisionRoute(
        IApplicationInputPort input,
        CompiledApplicationLink link)
    {
        if (input is IApplicationSignalInputPort signalInput)
            return new ApplicationSignalRevisionRoute<T>(this, signalInput, link);

        if (input is not ApplicationInputPort<T> typedInput)
        {
            throw new InvalidOperationException(
                $"Link '{link.Source}' to '{link.Target}' requires exact payload type '{typeof(T)}'.");
        }

        return new ApplicationMessageRevisionRoute<T>(this, typedInput, link);
    }

    public ApplicationOutputReceiveRegistration<T> RegisterReceive(TraceId? traceId)
    {
        lock (_gate)
        {
            if (_completeRequested || _aborted)
            {
                return ApplicationOutputReceiveRegistration<T>.Completed(new PortReceiveResult<T>
                {
                    Port = Address,
                    Status = PortReceiveStatus.Completed
                });
            }

            if (_activeSources == 0 && _ingress.Count == 0)
            {
                return ApplicationOutputReceiveRegistration<T>.Completed(new PortReceiveResult<T>
                {
                    Port = Address,
                    Status = PortReceiveStatus.Unavailable
                });
            }

            var waiter = new ApplicationOutputReceiveWaiter<T>(this, traceId);
            _waiters.Add(waiter);
            return new ApplicationOutputReceiveRegistration<T>(waiter);
        }
    }

    public PortObserveResult<T> Observe(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Observation capacity must be greater than zero.");

        lock (_gate)
        {
            if (_completeRequested || _aborted)
            {
                return new PortObserveResult<T>
                {
                    Port = Address,
                    Status = PortObserveStatus.Completed
                };
            }

            if (_activeSources == 0 && _ingress.Count == 0)
            {
                return new PortObserveResult<T>
                {
                    Port = Address,
                    Status = PortObserveStatus.Unavailable
                };
            }

            var observation = new PortObservation<T>(Address, capacity, RemoveObservation);
            _observations.Add(observation);
            return new PortObserveResult<T>
            {
                Port = Address,
                Status = PortObserveStatus.Started,
                Observation = observation
            };
        }
    }

    public void Complete()
    {
        ApplicationOutputAttachment<T>[] attachments;
        lock (_gate)
        {
            if (_completeRequested || _aborted)
                return;

            _completeRequested = true;
            attachments = _attachments.ToArray();
        }

        foreach (var attachment in attachments)
            attachment.Dispose();
        _ingress.Complete();
    }

    public void Abort()
    {
        ApplicationOutputAttachment<T>[] attachments;
        lock (_gate)
        {
            if (_aborted)
                return;

            _aborted = true;
            _completeRequested = true;
            attachments = _attachments.ToArray();
        }

        foreach (var attachment in attachments)
            attachment.Dispose();

        _abort.Cancel();
        ((IDataflowBlock)_ingress).Fault(new OperationCanceledException("The application port runtime was disposed."));
        FinishSubscribers();
        _completion.TrySetResult();
    }

    internal async ValueTask DrainAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await _dispatchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_ingress.Count == 0)
                    return;
            }
            finally
            {
                _dispatchGate.Release();
            }

            await Task.Yield();
        }
    }

    private async Task PumpAsync()
    {
        try
        {
            while (await _ingress.OutputAvailableAsync(_abort.Token).ConfigureAwait(false))
            {
                while (true)
                {
                    await _dispatchGate.WaitAsync(_abort.Token).ConfigureAwait(false);
                    try
                    {
                        if (!_ingress.TryReceive(out var message))
                            break;
                        Dispatch(message);
                    }
                    finally
                    {
                        _dispatchGate.Release();
                    }
                }
            }

            await _ingress.Completion.ConfigureAwait(false);
            FinishSubscribers();
            _completion.TrySetResult();
        }
        catch (OperationCanceledException) when (_abort.IsCancellationRequested)
        {
            _completion.TrySetResult();
        }
        catch (Exception exception)
        {
            FinishSubscribers(exception.GetBaseException());
            _completion.TrySetException(exception.GetBaseException());
        }
    }

    private void Dispatch(FlowMessage<T> message)
    {
        _activity(new ApplicationPortActivity(
            DateTimeOffset.UtcNow,
            ApplicationPortActivityKind.OutputEmitted,
            Address,
            RelatedPort: null,
            message.CorrelationId,
            message.TraceId,
            message.MessageId));

        IApplicationOutputLink<T>[] links;
        var revisionLinks = _revisionRouting.GetRoutes(Address);
        ApplicationOutputReceiveWaiter<T>[] waiters;
        PortObservation<T>[] observations;
        lock (_gate)
        {
            links = _links.ToArray();
            waiters = _waiters.ToArray();
            observations = _observations.ToArray();
        }

        foreach (var link in revisionLinks)
            link.TryDeliver(message);

        foreach (var link in links)
            link.TryDeliver(message);

        foreach (var waiter in waiters)
            waiter.TryDeliver(message);

        foreach (var observation in observations)
        {
            if (observation.TryPost(message))
                continue;
            if (!observation.IsActive)
                continue;

            var exception = new InvalidOperationException(
                $"Observation of output port '{Address}' exceeded its capacity of {observation.Capacity}.");
            observation.Fault(exception);
            _report(new ApplicationPortRejection
            {
                Timestamp = DateTimeOffset.UtcNow,
                Port = Address,
                CorrelationId = message.CorrelationId,
                TraceId = message.TraceId,
                MessageId = message.MessageId,
                Reason = ApplicationPortRejectionReason.ObservationOverflowed,
                Exception = exception
            });
        }
    }

    private void FinishSubscribers(Exception? exception = null)
    {
        ApplicationOutputReceiveWaiter<T>[] waiters;
        PortObservation<T>[] observations;
        lock (_gate)
        {
            waiters = _waiters.ToArray();
            observations = _observations.ToArray();
            _waiters.Clear();
            _observations.Clear();
        }

        foreach (var waiter in waiters)
            waiter.Complete();

        foreach (var observation in observations)
        {
            if (exception is null)
                observation.Complete();
            else
                observation.Fault(exception);
        }
    }

    internal void RemoveLink(IApplicationOutputLink<T> link)
    {
        lock (_gate)
            _links.Remove(link);
    }

    internal void RemoveWaiter(ApplicationOutputReceiveWaiter<T> waiter)
    {
        lock (_gate)
            _waiters.Remove(waiter);
    }

    private void RemoveObservation(PortObservation<T> observation)
    {
        lock (_gate)
            _observations.Remove(observation);
    }

    internal void RemoveAttachment(ApplicationOutputAttachment<T> attachment)
    {
        lock (_gate)
        {
            if (_attachments.Remove(attachment))
                _activeSources--;
        }
    }

    internal void ReportSourceFault(Exception exception)
        => _report(new ApplicationPortRejection
        {
            Timestamp = DateTimeOffset.UtcNow,
            Port = Address,
            Reason = ApplicationPortRejectionReason.SourceFaulted,
            Exception = exception
        });

    internal void ReleaseRevision() => _dispatchGate.Release();

    internal void TryDeliver(
        ApplicationInputPort<T> target,
        CompiledApplicationLink link,
        FlowMessage<T> message)
    {
        object? input = message.IsError ? message.Error : message.Value;
        var context = new FlowMapContext
        {
            Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["input"] = input,
                ["message"] = message,
                ["payload"] = input
            }
        };

        if (!link.TryMatch(context, out var exception))
        {
            if (exception is not null)
            {
                _report(new ApplicationPortRejection
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    Port = Address,
                    RelatedPort = target.Address,
                    CorrelationId = message.CorrelationId,
                    TraceId = message.TraceId,
                    MessageId = message.MessageId,
                    Reason = ApplicationPortRejectionReason.ConditionFailed,
                    Exception = exception
                });
            }

            return;
        }

        target.TrySend(message, Address);
    }

    internal void TryDeliver(
        IApplicationSignalInputPort target,
        CompiledApplicationLink link,
        FlowMessage<T> message)
    {
        object? input = message.IsError ? message.Error : message.Value;
        var context = new FlowMapContext
        {
            Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["input"] = input,
                ["message"] = message,
                ["payload"] = input
            }
        };

        if (!link.TryMatch(context, out var exception))
        {
            if (exception is not null)
            {
                _report(new ApplicationPortRejection
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    Port = Address,
                    RelatedPort = target.Address,
                    CorrelationId = message.CorrelationId,
                    TraceId = message.TraceId,
                    MessageId = message.MessageId,
                    Reason = ApplicationPortRejectionReason.ConditionFailed,
                    Exception = exception
                });
            }

            return;
        }

        target.TrySend(message, Address);
    }
}
