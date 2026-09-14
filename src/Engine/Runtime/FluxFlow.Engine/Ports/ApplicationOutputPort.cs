using System.Text.Json;
using FluxFlow.Data;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Addressing;
using FluxFlow.Composition.Links;
using FluxFlow.Core;
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

    Task DispatchCompletion { get; }

    void ThrowIfFaulted();

    void Activate();

    ValueTask DrainAsync(CancellationToken cancellationToken);
}

internal sealed class ApplicationOutputPort : IApplicationOutputPort
{
    private readonly object _gate = new();
    private readonly FlowOutput<FlowMessage> _output;
    private readonly Action<ApplicationPortRejection> _report;
    private readonly Action<ApplicationPortActivity> _activity;
    private readonly ApplicationRevisionRouting _revisionRouting;
    private readonly IApplicationOutputCapture? _capture;
    private readonly FlowRequestReply? _exchanges;
    private readonly bool _discardUnmatched;
    private bool _isolatedConditionFailure;
    private readonly List<ApplicationOutputLink> _links = [];
    private readonly List<ApplicationOutputReceiveWaiter> _waiters = [];
    private readonly List<IPortObservation> _observations = [];
    private readonly List<ApplicationOutputAttachment> _attachments = [];
    private readonly CancellationTokenSource _abort = new();
    private readonly Action _checkDeliveryCancellation;
    private readonly SemaphoreSlim _dispatchGate = new(1, 1);
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private bool _completeRequested;
    private bool _aborted;
    private int _activeSources;

    public ApplicationOutputPort(
        ApplicationAddress address,
        int capacity,
        Action<ApplicationPortRejection> report,
        Action<ApplicationPortActivity> activity,
        ApplicationRevisionRouting revisionRouting,
        IApplicationOutputCapture? capture = null,
        FlowRequestReply? exchanges = null,
        bool discardUnmatched = false)
    {
        Address = address;
        Capacity = capacity;
        _report = report;
        _activity = activity;
        _revisionRouting = revisionRouting;
        _capture = capture;
        _exchanges = exchanges;
        _discardUnmatched = discardUnmatched;
        _checkDeliveryCancellation = CheckDeliveryCancellation;
        _output = new FlowOutput<FlowMessage>(new FlowOutputOptions { Capacity = capacity }, capacityIncludesActive: true);
        _output.ConfigureDispatch(PrepareDispatchAsync);
        _ = ObserveDispatchAsync();
    }

    public ApplicationAddress Address { get; }

    private int _requestReplies;
    internal void EnableRequestReplies() => Volatile.Write(ref _requestReplies, 1);

    public Type PayloadType => typeof(global::FluxFlow.Data.FlowValue);

    public int Capacity { get; }

    public Task Completion => _completion.Task;

    private int PendingWork => _output.PendingPreparedMessages;

    public ApplicationPortStatus GetStatus()
    {
        lock (_gate)
        {
            return new ApplicationPortStatus
            {
                Address = Address,
                Direction = ApplicationPortDirection.Output,
                Kind = ApplicationPortKind.Message,
                PayloadType = typeof(global::FluxFlow.Data.FlowValue),
                Capacity = Capacity,
                PendingMessages = _aborted ? 0 : PendingWork,
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

        return new ApplicationOutputRevision(this);
    }

    public IPreparedApplicationOutput PrepareRevisionOutput(object source)
    {
        if (source is not ISourceBlock<FlowMessage> typedSource)
        {
            throw new InvalidOperationException(
                $"Output port '{Address}' requires source payload type '{typeof(global::FluxFlow.Data.FlowValue)}'.");
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_aborted, this);
            if (_completeRequested)
                throw new InvalidOperationException($"Output port '{Address}' is completed.");
        }

        return new FlowValuePreparedApplicationOutput(this, typedSource);
    }

    public IDisposable Attach(ISourceBlock<FlowMessage> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        ApplicationOutputAttachment attachment;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_aborted, this);
            if (_completeRequested)
                throw new InvalidOperationException($"Output port '{Address}' is completed.");

            var link = source.LinkTo(
                (ITargetBlock<FlowMessage>)_output.Ingress,
                new DataflowLinkOptions { PropagateCompletion = false });
            attachment = new ApplicationOutputAttachment(this, link);
            _attachments.Add(attachment);
            _activeSources++;
        }

        _ = source.Completion.ContinueWith(
            static (task, state) =>
            {
                var value = (ApplicationOutputAttachment)state!;
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
        var outputLink = new ApplicationOutputLink(this, CreateRevisionRoute(input, link));
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_aborted, this);
            if (_completeRequested)
                throw new InvalidOperationException($"Output port '{Address}' is completed.");
            if (link.IsFallback && (_links.Any(existing => existing.IsFallback) ||
                _revisionRouting.GetRoutes(Address).Any(existing => existing.IsFallback)))
                throw new InvalidOperationException($"Output '{Address}' already has a fallback.");
            _links.Add(outputLink);
        }

        return outputLink;
    }

    public IApplicationRevisionRoute CreateRevisionRoute(
        IApplicationInputPort input,
        CompiledApplicationLink link)
        => new ApplicationInputRevisionRoute(this, input, link);

    internal void ValidateRevisionRoutes(IReadOnlyList<IApplicationRevisionRoute> routes)
    {
        lock (_gate)
        {
            if (routes.Count(route => route.IsFallback) + _links.Count(route => route.IsFallback) > 1)
                throw new InvalidOperationException($"Output '{Address}' already has a fallback.");
        }
    }

    public ApplicationOutputReceiveRegistration RegisterReceive()
    {
        lock (_gate)
        {
            if (_completeRequested || _aborted)
            {
                return ApplicationOutputReceiveRegistration.Completed(new ApplicationOutputReceiveResult(PortReceiveStatus.Completed));
            }

            if (_activeSources == 0 && PendingWork == 0)
            {
                return ApplicationOutputReceiveRegistration.Completed(new ApplicationOutputReceiveResult(PortReceiveStatus.Unavailable));
            }

            var waiter = new ApplicationOutputReceiveWaiter(this);
            _waiters.Add(waiter);
            return new ApplicationOutputReceiveRegistration(waiter);
        }
    }

    public PortObserveResult<T> Observe<T>(int capacity)
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

            if (_activeSources == 0 && PendingWork == 0)
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
        ApplicationOutputAttachment[] attachments;
        lock (_gate)
        {
            if (_completeRequested || _aborted)
                return;

            _completeRequested = true;
            attachments = _attachments.ToArray();
        }

        foreach (var attachment in attachments)
            attachment.Dispose();
        _output.Complete();
    }

    public void Abort()
    {
        ApplicationOutputAttachment[] attachments;
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
        _output.Fault(new OperationCanceledException("The application port runtime was disposed."));
        FinishSubscribers();
        _completion.TrySetResult();
    }

    internal async ValueTask DrainAsync(CancellationToken cancellationToken)
    {
        using var admissionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _abort.Token);
        try
        {
            await _output.DrainAsync(admissionCancellation.Token).ConfigureAwait(false);
            await _dispatchGate.WaitAsync(admissionCancellation.Token).ConfigureAwait(false);
            _dispatchGate.Release();
        }
        catch (OperationCanceledException) when (
            _abort.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            await Completion.ConfigureAwait(false);
        }
    }

    private async ValueTask<FlowOutputDispatch<FlowMessage>> PrepareDispatchAsync(
        FlowMessage message, CancellationToken cancellationToken)
    {
        await _dispatchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_capture is not null && !message.IsError)
            {
                try { _ = message.Value.Json; }
                catch (Exception exception) when (!_abort.IsCancellationRequested)
                {
                    _report(new ApplicationPortRejection
                    {
                        Timestamp = DateTimeOffset.UtcNow,
                        Port = Address,
                        CorrelationId = message.CorrelationId,
                        TraceId = message.TraceId,
                        MessageId = message.MessageId,
                        Reason = ApplicationPortRejectionReason.OutputCaptureFailed,
                        Exception = exception
                    });
                    message = message.WithError(new FlowError(
                        "flow.value.serialization_failed",
                        "The workflow value could not be represented as JSON.",
                        "Serialization",
                        details: JsonSerializer.SerializeToElement(new { reason = exception.Message })));
                }
            }
            await CaptureAsync(message).ConfigureAwait(false);
            return PrepareDelivery(message);
        }
        finally
        {
            // Freeze capture/selection only. Core joins target admission after this
            // gate is released, so in-flight routes survive revision activation.
            _dispatchGate.Release();
        }
    }

    private async Task ObserveDispatchAsync()
    {
        try
        {
            await _output.Completion.ConfigureAwait(false);
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

    private async ValueTask CaptureAsync(FlowMessage message)
    {
        if (_capture is not null)
        {
            try
            {
                await _capture.CaptureAsync(message, _abort.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_abort.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _report(new ApplicationPortRejection
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    Port = Address,
                    CorrelationId = message.CorrelationId,
                    TraceId = message.TraceId,
                    MessageId = message.MessageId,
                    Reason = ApplicationPortRejectionReason.OutputCaptureFailed,
                    Exception = exception
                });
                throw;
            }
        }

        // A capture may not cooperate with cancellation. Its eventual success does
        // not authorize delivery after the port has been aborted.
        _abort.Token.ThrowIfCancellationRequested();
    }

    private FlowOutputDispatch<FlowMessage> PrepareDelivery(FlowMessage message)
    {
        _activity(new ApplicationPortActivity(
            DateTimeOffset.UtcNow,
            ApplicationPortActivityKind.OutputEmitted,
            Address,
            RelatedPort: null,
            message.CorrelationId,
            message.TraceId,
            message.MessageId));

        ApplicationOutputLink[] links;
        var routing = _revisionRouting.Current;
        var revisionLinks = routing.Routes.GetValueOrDefault(Address) ?? [];
        ApplicationOutputReceiveWaiter[] waiters;
        IPortObservation[] observations;
        lock (_gate)
        {
            links = _links.ToArray();
            waiters = _waiters.ToArray();
            observations = _observations.ToArray();
        }

        // Evaluate every condition before any business target can receive this item.
        var selected = SelectRoutes(message, revisionLinks, links,
            _discardUnmatched || routing.DiscardedOutputs.Contains(Address));
        return new FlowOutputDispatch<FlowMessage>(selected,
            waiters.Length == 0 && observations.Length == 0
                ? _checkDeliveryCancellation
                : CreateDeliveryNotification(message, waiters, observations));
    }

    // Keep the empty-observer path free of per-message closure allocation.
    private Action CreateDeliveryNotification(FlowMessage message,
        ApplicationOutputReceiveWaiter[] waiters, IPortObservation[] observations)
        => () => NotifyDelivered(message, waiters, observations);

    private void CheckDeliveryCancellation() => _abort.Token.ThrowIfCancellationRequested();

    private void NotifyDelivered(FlowMessage message,
        ApplicationOutputReceiveWaiter[] waiters, IPortObservation[] observations)
    {
        CheckDeliveryCancellation();

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

    private IApplicationRevisionRoute[] SelectRoutes(FlowMessage message,
        IReadOnlyList<IApplicationRevisionRoute> revisionLinks, ApplicationOutputLink[] links,
        bool discardUnmatched)
    {
        var routes = revisionLinks.Concat(links).ToArray();
        Span<bool> selected = routes.Length <= 128 ? stackalloc bool[routes.Length] : new bool[routes.Length];
        _isolatedConditionFailure = false;
        var selection = FlowRouteSelection.SelectOrdinary(routes, selected, message,
            static route => route.IsFallback, static (route, value) => route.IsMatch(value));
        var requestReplies = Volatile.Read(ref _requestReplies) != 0;
        var isReply = requestReplies && _exchanges?.Reply(Address.Value, message) == true;
        if (selection.Matches == 0 && !isReply && selection.Fallback >= 0 && !routes[selection.Fallback].IsMatch(message))
            selection.Fallback = -1;
        var count = FlowRouteSelection.Complete(selected, selection, Address.Value,
            discardUnmatched || _isolatedConditionFailure || requestReplies, isReply);
        if (count == routes.Length) return routes;
        if (count == 0) return [];
        var result = new IApplicationRevisionRoute[count];
        for (int index = 0, target = 0; index < routes.Length; index++)
            if (selected[index]) result[target++] = routes[index];
        return result;
    }

    private void FinishSubscribers(Exception? exception = null)
    {
        ApplicationOutputReceiveWaiter[] waiters;
        IPortObservation[] observations;
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

    internal void RemoveLink(ApplicationOutputLink link)
    {
        lock (_gate)
            _links.Remove(link);
    }

    internal void RemoveWaiter(ApplicationOutputReceiveWaiter waiter)
    {
        lock (_gate)
            _waiters.Remove(waiter);
    }

    private void RemoveObservation(IPortObservation observation)
    {
        lock (_gate)
            _observations.Remove(observation);
    }

    internal void RemoveAttachment(ApplicationOutputAttachment attachment)
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

    internal async ValueTask DeliverAsync(
        IApplicationInputPort target,
        CompiledApplicationLink link,
        FlowMessage message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = link.Delivery == FlowDeliveryPolicy.Reliable
            ? await target.SendAsync(message, Address, cancellationToken).ConfigureAwait(false)
            : target.TrySend(message, Address);
        EnsureDelivered(result, link);
    }

    private static void EnsureDelivered(PortSendResult result, CompiledApplicationLink link)
    {
        if (!result.IsAccepted && link.Delivery != FlowDeliveryPolicy.IsolateRejection)
            throw new InvalidOperationException(
                $"Link '{link.Source}' to '{link.Target}' rejected delivery: {result.Status}.");
    }

    internal static FlowMessage<global::FluxFlow.Data.FlowValue> ToTypedMessage(FlowMessage message)
        => ApplicationMessageProjection.ToTyped<global::FluxFlow.Data.FlowValue>(message);

    internal bool IsMatch(
        CompiledApplicationLink link,
        FlowMessage message)
    {
        if (!link.IsConditional)
            return true;

        var context = new FlowMapContext
        {
            Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["input"] = message.Value,
                ["message"] = message,
                ["payload"] = message.Value
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
                    RelatedPort = link.Target,
                    CorrelationId = message.CorrelationId,
                    TraceId = message.TraceId,
                    MessageId = message.MessageId,
                    Reason = ApplicationPortRejectionReason.ConditionFailed,
                    Exception = exception
                });
                if (link.Delivery != FlowDeliveryPolicy.IsolateRejection)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
                _isolatedConditionFailure = true;
            }

            return false;
        }

        return true;
    }
}
