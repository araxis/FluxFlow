using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition.Definition;
using FluxFlow.Data;
using FluxFlow.Core;
using FluxFlow.Core.Addressing;
using FluxFlow.Composition.Links;
using FluxFlow.Engine.Internal.Revisions;
using FluxFlow.Engine.Signals;
using FluxFlow.Nodes;
using Microsoft.Extensions.Logging;

namespace FluxFlow.Engine.Ports;

internal sealed class ApplicationPortRuntime : IAsyncDisposable
{
    private const int RejectionCapacity = 256;

    private readonly IReadOnlyDictionary<ApplicationAddress, IApplicationInputPort> _inputs;
    private readonly IReadOnlyDictionary<ApplicationAddress, IApplicationOutputPort> _outputs;
    private readonly IReadOnlyDictionary<ApplicationAddress, ApplicationPortMetadata> _metadataByAddress;
    private readonly ApplicationRevisionRouting _revisionRouting = new();
    private readonly FlowRequestReply _exchanges;
    private readonly ApplicationRuntimeSignals _signals;
    private readonly ApplicationPortEventPublisher _events;
    private readonly BufferBlock<ApplicationPortRejection> _rejections = new(
        new DataflowBlockOptions { BoundedCapacity = RejectionCapacity });
    private readonly List<IDisposable> _links = [];
    private readonly object _gate = new();
    private readonly SemaphoreSlim _revisionGate = new(1, 1);
    private readonly Task _completion;
    private readonly object _statusGate = new();
    private ApplicationRuntimeState _state = ApplicationRuntimeState.Active;
    private DateTimeOffset _stateChangedAt = DateTimeOffset.UtcNow;
    private Exception? _completionFailure;
    private int _completeRequested;
    private int _disposed;
    private long _revisionSequence;
    private ApplicationPortRevisionInfo? _currentRevision;

    internal ApplicationPortRuntime(
        IReadOnlyList<ApplicationPortRuntimeBuilder.PortRegistration> registrations,
        ILogger? logger,
        int requestCapacity = 1024,
        TimeProvider? timeProvider = null)
    {
        _exchanges = new FlowRequestReply(requestCapacity, timeProvider);
        var metadata = new Dictionary<ApplicationAddress, ApplicationPortMetadata>();

        foreach (var registration in registrations)
        {
            var portMetadata = new ApplicationPortMetadata
            {
                Address = registration.Address,
                Direction = registration.Direction,
                Kind = registration.Kind,
                PayloadType = registration.PayloadType,
                Capacity = registration.Capacity
            };
            metadata.Add(registration.Address, portMetadata);
        }

        _metadataByAddress = metadata;
        Ports = metadata.Values
            .OrderBy(static value => value.Address.Value, StringComparer.Ordinal)
            .ToArray();
        _signals = new ApplicationRuntimeSignals(logger);
        _events = new ApplicationPortEventPublisher(
            metadata,
            _signals,
            rejection => _rejections.Post(rejection));

        var inputs = new Dictionary<ApplicationAddress, IApplicationInputPort>();
        var outputs = new Dictionary<ApplicationAddress, IApplicationOutputPort>();
        foreach (var registration in registrations)
        {

            if (registration.Direction == ApplicationPortDirection.Input)
            {
                inputs.Add(
                    registration.Address,
                    registration.CreateInput!(
                        _events.ReportRejection,
                        _events.ReportActivity));
            }
            else
            {
                outputs.Add(
                    registration.Address,
                    registration.CreateOutput!(
                        _events.ReportRejection,
                        _events.ReportActivity,
                        _revisionRouting,
                        _exchanges));
            }
        }

        _inputs = inputs;
        _outputs = outputs;
        GetOutput(ApplicationAddress.SystemEvents)
            .Attach(_signals.SystemEvents);
        GetOutput(ApplicationAddress.SystemDiagnostics)
            .Attach(_signals.Diagnostics);
        _completion = CompleteRuntimeAsync();
    }

    public IReadOnlyList<ApplicationPortMetadata> Ports { get; }

    public ISourceBlock<ApplicationPortRejection> Rejections => _rejections;

    public ISourceBlock<FlowMessage> SystemEvents => _signals.SystemEvents;

    public ISourceBlock<FlowMessage> Diagnostics => _signals.Diagnostics;

    public ISourceBlock<FlowMessage> Activity => _signals.Activity;

    internal void ForwardEventsTo(Events.ApplicationEventStream events)
        => _signals.EventObserver = message =>
        {
            if (message.Headers.GetValueOrDefault(FlowEventHeaders.Type) != ApplicationSystemEventNames.RevisionChanged)
                events.Publish(message);
        };

    public Task Completion => _completion;

    public ApplicationPortRevisionInfo? CurrentRevision => Volatile.Read(ref _currentRevision);

    public ApplicationRuntimeStatus Status
    {
        get
        {
            ApplicationRuntimeState state;
            DateTimeOffset changedAt;
            lock (_statusGate)
            {
                state = _state;
                changedAt = _stateChangedAt;
            }

            return new ApplicationRuntimeStatus
            {
                State = state,
                ChangedAt = changedAt,
                Ports = _inputs.Values.Select(static port => port.GetStatus())
                    .Concat(_outputs.Values.Select(static port => port.GetStatus()))
                    .OrderBy(static port => port.Address.Value, StringComparer.Ordinal)
                    .ToArray()
            };
        }
    }

    internal ValueTask<SystemEventPublishResult> PublishSystemEventAsync(
        FlowMessage message,
        CancellationToken cancellationToken = default)
        => _signals.PublishSystemEventAsync(message, cancellationToken);

    internal async ValueTask<bool> PublishAsync(
        ApplicationRevisionEvent revisionEvent,
        CancellationToken cancellationToken = default)
        => (await PublishSystemEventAsync(CreateRevisionMessage(revisionEvent), cancellationToken)
            .ConfigureAwait(false)).IsAccepted;

    internal static FlowMessage CreateRevisionMessage(ApplicationRevisionEvent revisionEvent)
    {
        ArgumentNullException.ThrowIfNull(revisionEvent);
        var details = JsonSerializer.SerializeToElement(new
        {
            phase = revisionEvent.Phase.ToString(),
            resources = revisionEvent.Resources.Select(static resource => resource.Value).ToArray(),
            sequence = revisionEvent.Sequence,
            workflows = revisionEvent.Workflows.ToArray()
        });
        return ApplicationSignalMessage.Create(new FlowEvent
        {
            Timestamp = revisionEvent.Timestamp,
            Name = ApplicationSystemEventNames.RevisionChanged,
            Kind = FlowEventKind.Lifecycle,
            Level = revisionEvent.Error is null ? FlowEventLevel.Information : FlowEventLevel.Warning,
            Dimensions = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["category"] = "Revision",
                ["subject"] = revisionEvent.RevisionId
            },
            Attributes = revisionEvent.Error is null
                ? new Dictionary<string, object?>(StringComparer.Ordinal)
                : new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["error"] = revisionEvent.Error
                },
            Details = ((FlowValue)new JsonFlowValue(details))
        });
    }

    internal bool TryPublishDiagnostic(FlowMessage message)
        => _signals.TryPublishDiagnostic(message);

    internal void PublishActivity(FlowMessage message)
        => _signals.TryPublishActivity(message);

    public ApplicationPortRevisionBuilder CreateRevision(string revisionId)
    {
        ThrowIfDisposed();
        return new ApplicationPortRevisionBuilder(this, revisionId);
    }

    public ValueTask<PortSendResult> SendAsync<T>(
        InputPortHandle<T> input,
        FlowMessage<T> message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        return SendAsync(input.Address, message, cancellationToken);
    }

    public ValueTask<PortSendResult> SendAsync(
        InputPortHandle<global::FluxFlow.Data.FlowValue> input,
        FlowMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        return SendAsync(input.Address, message, cancellationToken);
    }

    public ValueTask<PortSendResult> SendAsync<T>(
        SignalInputPortHandle input,
        FlowMessage<T> message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        return SendAsync(input.Address, message, cancellationToken);
    }

    public ValueTask<PortSendResult> SendAsync<T>(
        ApplicationAddress input,
        FlowMessage<T> message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();
        var inputPort = GetInputPort(input);
        if (Volatile.Read(ref _completeRequested) != 0)
        {
            return ValueTask.FromResult(ReportCompletedInput(inputPort.Address,
                message.CorrelationId, message.TraceId, message.MessageId));
        }
        var canonical = message.IsError
            ? message.ConvertError()
            : message.ConvertValue((message.Value is FlowValue flowValue ? flowValue : message.Value is JsonElement json ? (FlowValue)new JsonFlowValue(json) : message.Value is JsonDocument document ? new JsonFlowValue(document.RootElement) : (FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(message.Value, ValueJsonOptions))));
        return inputPort.SendAsync(canonical, source: null, cancellationToken);
    }

    public ValueTask<PortSendResult> SendAsync(
        ApplicationAddress input,
        FlowMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();
        var inputPort = GetInputPort(input);
        if (Volatile.Read(ref _completeRequested) != 0)
        {
            return ValueTask.FromResult(ReportCompletedInput(inputPort.Address,
                message.CorrelationId, message.TraceId, message.MessageId));
        }

        return inputPort.SendAsync(message, source: null, cancellationToken);
    }

    /// <summary>Attempts immediate admission without waiting for input capacity.</summary>
    public PortSendResult TrySend<T>(ApplicationAddress input, FlowMessage<T> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var inputPort = GetInputPort(input);
        if (Volatile.Read(ref _completeRequested) != 0)
            return ReportCompletedInput(inputPort.Address, message.CorrelationId, message.TraceId, message.MessageId);

        var canonical = message.IsError ? message.ConvertError()
            : message.ConvertValue((message.Value is FlowValue flowValue ? flowValue : message.Value is JsonElement json ? (FlowValue)new JsonFlowValue(json) : message.Value is JsonDocument document ? new JsonFlowValue(document.RootElement) : (FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(message.Value, ValueJsonOptions))));
        return inputPort.TrySend(canonical);
    }

    /// <summary>Attempts immediate canonical admission without waiting for input capacity.</summary>
    public PortSendResult TrySend(ApplicationAddress input, FlowMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var inputPort = GetInputPort(input);
        if (Volatile.Read(ref _completeRequested) != 0)
            return ReportCompletedInput(inputPort.Address, message.CorrelationId, message.TraceId, message.MessageId);
        return inputPort.TrySend(message);
    }

    private PortSendResult ReportCompletedInput(ApplicationAddress input,
        CorrelationId? correlationId, TraceId traceId, MessageId messageId)
    {
        _events.ReportRejection(new ApplicationPortRejection
        {
            Timestamp = DateTimeOffset.UtcNow,
            Port = input,
            CorrelationId = correlationId,
            TraceId = traceId,
            MessageId = messageId,
            Reason = ApplicationPortRejectionReason.Completed
        });
        return new PortSendResult { Port = input, Status = PortSendStatus.Completed };
    }

    public async Task<PortReceiveResult<T>> ReceiveAsync<T>(
        ApplicationAddress output,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateTimeout(timeout);

        if (!_outputs.TryGetValue(output, out var outputPort))
            throw CreatePortException(output, ApplicationPortDirection.Output);

        if (outputPort is ApplicationOutputPort flowValueOutput)
        {
            using var registration = flowValueOutput.RegisterReceive();
            var result = await WaitForReceiveAsync(
                    registration,
                    timeout,
                    cancellationToken)
                .ConfigureAwait(false);
            return new PortReceiveResult<T>
            {
                Port = output,
                Status = result.Status,
                Message = result.Message is null ? null : ApplicationMessageProjection.ToTyped<T>(result.Message)
            };
        }

        throw CreateTypeException(output, typeof(T), outputPort.PayloadType);
    }

    public Task<PortReceiveResult<T>> ReceiveAsync<T>(
        OutputPortHandle<T> output,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        return ReceiveAsync<T>(output.Address, timeout, cancellationToken);
    }

    public ValueTask<PortObserveResult<T>> ObserveAsync<T>(
        ApplicationAddress output,
        int capacity = 128,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_outputs.TryGetValue(output, out var outputPort))
            throw CreatePortException(output, ApplicationPortDirection.Output);
        if (outputPort is ApplicationOutputPort canonical)
            return ValueTask.FromResult(canonical.Observe<T>(capacity));

        throw CreateTypeException(output, typeof(T), outputPort.PayloadType);
    }

    public ValueTask<PortObserveResult<T>> ObserveAsync<T>(
        OutputPortHandle<T> output,
        int capacity = 128,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        return ObserveAsync<T>(output.Address, capacity, cancellationToken);
    }

    public async Task<PortRequestResult<TResponse>> SendAndReceiveAsync<TRequest, TResponse>(
        ApplicationAddress input,
        ApplicationAddress output,
        FlowMessage<TRequest> request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateTimeout(timeout);

        if (!_outputs.TryGetValue(output, out var outputPort))
            throw CreatePortException(output, ApplicationPortDirection.Output);
        if (outputPort is not ApplicationOutputPort replyPort)
            throw CreateTypeException(output, typeof(TResponse), outputPort.PayloadType);

        var status = outputPort.GetStatus();
        if (status.Availability == ApplicationPortAvailability.Completed ||
            Volatile.Read(ref _completeRequested) != 0 || Volatile.Read(ref _disposed) != 0)
            return Result(PortRequestStatus.OutputCompleted);
        if (status.Availability == ApplicationPortAvailability.Unavailable && status.PendingMessages == 0)
            return Result(PortRequestStatus.OutputUnavailable);

        var canonical = request.IsError ? request.ConvertError() : request.ConvertValue((request.Value is FlowValue flowValue ? flowValue : request.Value is JsonElement json ? (FlowValue)new JsonFlowValue(json) : request.Value is JsonDocument document ? new JsonFlowValue(document.RootElement) : (FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(request.Value, ValueJsonOptions))));
        using var requestStopped = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            var pending = _exchanges.RequestAsync(output.Value, canonical, async (message, token) =>
            {
                replyPort.EnableRequestReplies();
                var send = await SendAsync(input, message, token).ConfigureAwait(false);
                if (!send.IsAccepted)
                    throw new RequestAdmissionException(send.Status switch
                    {
                        PortSendStatus.Full => PortRequestStatus.InputFull,
                        PortSendStatus.Unavailable => PortRequestStatus.InputUnavailable,
                        PortSendStatus.Completed => PortRequestStatus.InputCompleted,
                        _ => throw new ArgumentOutOfRangeException(nameof(send.Status))
                    });
            }, timeout ?? Timeout.InfiniteTimeSpan, requestStopped.Token);

            if (await Task.WhenAny(pending, outputPort.Completion).ConfigureAwait(false) == outputPort.Completion &&
                !pending.IsCompleted)
                requestStopped.Cancel();

            var response = await pending.ConfigureAwait(false);
            return Result(PortRequestStatus.Received, ApplicationMessageProjection.ToTyped<TResponse>(response));
        }
        catch (RequestAdmissionException error) { return Result(error.Status); }
        catch (TimeoutException) { return Result(PortRequestStatus.TimedOut); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested &&
            (outputPort.Completion.IsCompleted || Volatile.Read(ref _completeRequested) != 0 ||
             Volatile.Read(ref _disposed) != 0))
        {
            return Result(PortRequestStatus.OutputCompleted);
        }
        PortRequestResult<TResponse> Result(PortRequestStatus resultStatus, FlowMessage<TResponse>? response = null)
        {
            _events.ReportRequest(resultStatus, request, input, output, startedAt);
            return new PortRequestResult<TResponse>
            {
                InputPort = input,
                OutputPort = output,
                Status = resultStatus,
                Response = response
            };
        }
    }

    private sealed class RequestAdmissionException(PortRequestStatus status) : Exception
    {
        public PortRequestStatus Status { get; } = status;
    }

    public Task<PortRequestResult<TResponse>> SendAndReceiveAsync<TRequest, TResponse>(
        InputPortHandle<TRequest> input,
        OutputPortHandle<TResponse> output,
        FlowMessage<TRequest> request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        return SendAndReceiveAsync<TRequest, TResponse>(
            input.Address,
            output.Address,
            request,
            timeout,
            cancellationToken);
    }

    public ValueTask<IAsyncDisposable> AttachInputAsync(
        ApplicationAddress input,
        ITargetBlock<FlowMessage> target,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(target);
        var port = GetInputPort(input);
        if (port is not ApplicationInputPort { Kind: ApplicationPortKind.Message } canonical)
            throw CreateTypeException(input, typeof(global::FluxFlow.Data.FlowValue), port.PayloadType);
        return canonical.AttachAsync(target, cancellationToken);
    }

    public ValueTask<IAsyncDisposable> AttachSignalInputAsync(
        SignalInputPortHandle input,
        ITargetBlock<FlowMessage> target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        return AttachSignalInputAsync(input.Address, target, cancellationToken);
    }

    public ValueTask<IAsyncDisposable> AttachSignalInputAsync(
        ApplicationAddress input,
        ITargetBlock<FlowMessage> target,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(target);
        return GetSignalInput(input).AttachAsync(target, cancellationToken);
    }

    public IDisposable AttachOutput(ApplicationAddress output, ISourceBlock<FlowMessage> source)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(source);
        return GetOutput(output).Attach(source);
    }

    public IDisposable Connect(CompiledApplicationLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        ThrowIfDisposed();

        if (!_outputs.TryGetValue(link.Source, out var output))
            throw CreatePortException(link.Source, ApplicationPortDirection.Output);
        if (!_inputs.TryGetValue(link.Target, out var input))
            throw CreatePortException(link.Target, ApplicationPortDirection.Input);
        if (output.PayloadType != link.MessageType ||
            (input.Kind == ApplicationPortKind.Message && output.PayloadType != input.PayloadType))
        {
            throw new InvalidOperationException(
                $"Link '{link.Source}' to '{link.Target}' requires exact payload type '{link.MessageType}', " +
                $"but the runtime ports use '{output.PayloadType}' and '{input.PayloadType}'.");
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            var connection = output.Connect(input, link);
            _links.Add(connection);
            return new RuntimeLink(this, connection);
        }
    }

    public void Complete()
    {
        lock (_gate)
        {
            if (Interlocked.Exchange(ref _completeRequested, 1) != 0)
                return;
        }

        _exchanges.Stop();
        SetState(ApplicationRuntimeState.Completing);
        _ = CompletePortsAsync();
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
        }

        await _exchanges.DisposeAsync().ConfigureAwait(false);
        await _revisionGate.WaitAsync().ConfigureAwait(false);

        try
        {
            IDisposable[] links;
            lock (_gate)
            {
                links = _links.ToArray();
                _links.Clear();
            }

            foreach (var link in links)
                link.Dispose();
            await _signals.DisposeAsync().ConfigureAwait(false);
            foreach (var output in _outputs.Values)
                output.Abort();
            foreach (var input in _inputs.Values)
                input.Abort();

            try
            {
                await _completion.ConfigureAwait(false);
            }
            finally
            {
                SetState(ApplicationRuntimeState.Disposed);
                _rejections.Complete();
            }
        }
        finally
        {
            _revisionGate.Release();
        }
    }

    internal void ValidateRevisionInput(ApplicationAddress address)
    {
        var input = GetInputPort(address);
        if (input is not ApplicationInputPort { Kind: ApplicationPortKind.Message })
            throw CreateTypeException(address, typeof(global::FluxFlow.Data.FlowValue), input.PayloadType);
    }

    internal void ValidateRevisionSignalInput(ApplicationAddress address)
        => _ = GetSignalInput(address);

    internal IPreparedApplicationOutput PrepareRevisionOutput(
        ApplicationAddress address,
        ISourceBlock<FlowMessage> source)
    {
        if (!_outputs.TryGetValue(address, out var output))
            throw CreatePortException(address, ApplicationPortDirection.Output);
        if (output is not ApplicationOutputPort canonical)
            throw CreateTypeException(address, typeof(global::FluxFlow.Data.FlowValue), output.PayloadType);
        return canonical.PrepareRevisionOutput(source);
    }

    internal ApplicationRevisionRouting.Snapshot PrepareRevisionRouting(
        IEnumerable<CompiledApplicationLink> links,
        IEnumerable<ApplicationAddress>? discardedOutputs = null)
    {
        var routes = new Dictionary<ApplicationAddress, List<IApplicationRevisionRoute>>();
        var identities = new HashSet<ApplicationRevisionRouting.RouteIdentity>();
        var declarations = new HashSet<ApplicationRevisionRouting.RouteIdentity>();
        foreach (var link in links)
        {
            ArgumentNullException.ThrowIfNull(link);
            if (!_outputs.TryGetValue(link.Source, out var output))
                throw CreatePortException(link.Source, ApplicationPortDirection.Output);
            if (!_inputs.TryGetValue(link.Target, out var input))
                throw CreatePortException(link.Target, ApplicationPortDirection.Input);
            if (output.PayloadType != link.MessageType ||
                (input.Kind == ApplicationPortKind.Message && output.PayloadType != input.PayloadType))
            {
                throw new InvalidOperationException(
                    $"Link '{link.Source}' to '{link.Target}' requires exact payload type '{link.MessageType}', " +
                    $"but the runtime ports use '{output.PayloadType}' and '{input.PayloadType}'.");
            }

            var identity = new ApplicationRevisionRouting.RouteIdentity(
                link.Source,
                link.Target,
                link.MessageType,
                link.ConditionExpression,
                link.Delivery, link.IsFallback)
            {
                Condition = link.IsConditional ? link : null
            };
            // Executable/policy changes identify revisions, not additional declarations.
            if (!declarations.Add(identity with
            { Delivery = FlowDeliveryPolicy.Reliable, IsFallback = false, Condition = null }))
                throw new InvalidOperationException($"Revision contains duplicate link '{link.Source}' to '{link.Target}'.");
            identities.Add(identity);
            if (!routes.TryGetValue(link.Source, out var values))
            {
                values = [];
                routes.Add(link.Source, values);
            }

            if (link.IsFallback && values.Any(route => route.IsFallback))
                throw new InvalidOperationException($"Output '{link.Source}' already has a fallback.");
            values.Add(output.CreateRevisionRoute(input, link));
        }

        var discarded = (discardedOutputs ?? []).ToArray();
        foreach (var address in discarded) _ = GetOutput(address);
        return new ApplicationRevisionRouting.Snapshot(
            routes.ToDictionary(
                static pair => pair.Key,
                static pair => (IReadOnlyList<IApplicationRevisionRoute>)pair.Value.ToArray()),
            identities, discarded);
    }

    internal async ValueTask<ApplicationPortRevisionLease> ActivateRevisionAsync(
        ApplicationPortRevision revision,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task>? beforeCommit)
    {
        await _revisionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var inputRevisions = new List<IApplicationInputRevision>();
        var outputRevisions = new List<IApplicationOutputRevision>();
        try
        {
            ThrowIfDisposed();
            if (Volatile.Read(ref _completeRequested) != 0)
                throw new InvalidOperationException("The application port runtime is completing.");
            if (string.Equals(CurrentRevision?.RevisionId, revision.RevisionId, StringComparison.Ordinal))
                throw new InvalidOperationException($"Revision '{revision.RevisionId}' is already active.");

            var currentRouting = _revisionRouting.Current;
            var nextRouting = revision.RoutingConfigured
                ? revision.Routing!
                : currentRouting;
            var affectedInputs = revision.InputReplacements.Keys
                .Concat(currentRouting.GetChangedTargets(nextRouting))
                .Distinct()
                .OrderBy(static address => address.Value, StringComparer.Ordinal)
                .ToArray();
            var affectedOutputs = revision.PreparedOutputs
                .Select(static output => output.Address)
                .Concat(currentRouting.GetChangedSources(nextRouting))
                .Distinct()
                .OrderBy(static address => address.Value, StringComparer.Ordinal)
                .ToArray();

            foreach (var address in affectedInputs)
            {
                inputRevisions.Add(await _inputs[address]
                    .BeginRevisionAsync(cancellationToken)
                    .ConfigureAwait(false));
            }

            foreach (var address in affectedOutputs)
            {
                outputRevisions.Add(await _outputs[address]
                    .BeginRevisionAsync(cancellationToken)
                    .ConfigureAwait(false));
            }

            lock (_gate)
                ValidateCommit();

            // Sources start against their bounded staging buffers, not the live routes.
            if (beforeCommit is not null)
                await beforeCommit(cancellationToken).ConfigureAwait(false);

            var inputAttachments = new List<IApplicationInputAttachment>();
            ApplicationPortRevisionInfo info;
            lock (_gate)
            {
                ValidateCommit();
                foreach (var output in revision.PreparedOutputs)
                    output.Activate();
                foreach (var inputRevision in inputRevisions)
                {
                    if (revision.InputReplacements.TryGetValue(inputRevision.Address, out var target))
                    {
                        inputAttachments.Add((IApplicationInputAttachment)
                            inputRevision.Commit(target));
                    }
                }

                if (revision.RoutingConfigured)
                    _revisionRouting.Swap(nextRouting);
                info = new ApplicationPortRevisionInfo
                {
                    Sequence = ++_revisionSequence,
                    RevisionId = revision.RevisionId,
                    ActivatedAt = DateTimeOffset.UtcNow
                };
                Volatile.Write(ref _currentRevision, info);
            }

            var outputs = revision.TransferPreparedOutputs();
            return new ApplicationPortRevisionLease(this, info, inputAttachments, outputs);

            void ValidateCommit()
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfDisposed();
                if (Volatile.Read(ref _completeRequested) != 0)
                    throw new InvalidOperationException("The application port runtime is completing.");
                foreach (var output in revision.PreparedOutputs)
                    output.ThrowIfFaulted();
                if (revision.RoutingConfigured)
                    foreach (var address in affectedOutputs)
                        GetOutput(address).ValidateRevisionRoutes(nextRouting.Routes.GetValueOrDefault(address) ?? []);
            }
        }
        finally
        {
            for (var index = outputRevisions.Count - 1; index >= 0; index--)
                await outputRevisions[index].DisposeAsync().ConfigureAwait(false);
            for (var index = inputRevisions.Count - 1; index >= 0; index--)
                await inputRevisions[index].DisposeAsync().ConfigureAwait(false);
            _revisionGate.Release();
        }
    }

    internal async ValueTask StopSystemRoutesAsync(ApplicationPortRevisionInfo revision, CancellationToken cancellationToken)
    {
        await _revisionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // A retired lease on a shared port surface must not detach its successor.
            if (CurrentRevision?.Sequence != revision.Sequence || Volatile.Read(ref _disposed) != 0)
                return;
            var current = _revisionRouting.Current;
            var sources = current.Routes.Keys
                .Where(static address => address.Kind == ApplicationAddressKind.SystemPort).ToArray();
            var gates = new List<IApplicationOutputRevision>();
            try
            {
                foreach (var source in sources)
                    gates.Add(await _outputs[source].BeginRevisionAsync(cancellationToken).ConfigureAwait(false));
                _revisionRouting.Swap(new ApplicationRevisionRouting.Snapshot(
                    current.Routes.Where(static pair => pair.Key.Kind != ApplicationAddressKind.SystemPort)
                        .ToDictionary(static pair => pair.Key, static pair => pair.Value),
                    current.Identities.Where(static route => route.Source.Kind != ApplicationAddressKind.SystemPort)
                        .ToHashSet(), current.DiscardedOutputs));
            }
            finally
            {
                for (var index = gates.Count - 1; index >= 0; index--)
                    await gates[index].DisposeAsync().ConfigureAwait(false);
            }
            // Inputs are still open here. Settle selected deliveries before their
            // drain closes admission; later system events remain externally observable.
            foreach (var source in sources)
                await ((ApplicationOutputPort)_outputs[source]).DrainAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _revisionGate.Release(); }
    }

    private async Task CompleteRuntimeAsync()
    {
        try
        {
            await Task.WhenAll(
                    _inputs.Values.Select(static port => port.Completion)
                        .Concat(_outputs.Values.Select(static port => port.Completion)))
                .ConfigureAwait(false);

            if (Volatile.Read(ref _completionFailure) is { } failure)
                ExceptionDispatchInfo.Capture(failure).Throw();

            SetState(ApplicationRuntimeState.Completed);
        }
        catch
        {
            SetState(ApplicationRuntimeState.Faulted);
            throw;
        }
        finally
        {
            _rejections.Complete();
        }
    }

    private async Task CompletePortsAsync()
    {
        try
        {
            await _signals.PublishSystemEventAsync(
                    ApplicationSignalMessage.Create(new FlowEvent
                    {
                        Timestamp = DateTimeOffset.UtcNow,
                        Name = ApplicationSystemEventNames.RuntimeCompleting,
                        Kind = FlowEventKind.Lifecycle,
                        Dimensions = new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["category"] = "Lifecycle",
                            ["subject"] = "runtime"
                        }
                    }),
                    CancellationToken.None)
                .ConfigureAwait(false);

            var normalOutputs = _outputs
                .Where(static item => item.Key.Kind != ApplicationAddressKind.SystemPort)
                .Select(static item => item.Value)
                .ToArray();
            foreach (var output in normalOutputs)
                output.Complete();
            await Task.WhenAll(normalOutputs.Select(static output => output.Completion))
                .ConfigureAwait(false);

            _signals.Complete();
            await _signals.Completion.ConfigureAwait(false);

            var systemOutputs = _outputs
                .Where(static item => item.Key.Kind == ApplicationAddressKind.SystemPort)
                .Select(static item => item.Value)
                .ToArray();
            foreach (var output in systemOutputs)
                output.Complete();
            await Task.WhenAll(systemOutputs.Select(static output => output.Completion))
                .ConfigureAwait(false);

            foreach (var input in _inputs.Values)
                input.Complete();
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _completionFailure, exception);
            foreach (var output in _outputs.Values)
                output.Abort();
            foreach (var input in _inputs.Values)
                input.Abort();
        }
    }

    private IApplicationInputPort GetInputPort(ApplicationAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!_inputs.TryGetValue(address, out var input))
            throw CreatePortException(address, ApplicationPortDirection.Input);
        return input;
    }

    private ApplicationInputPort GetSignalInput(ApplicationAddress address)
    {
        var input = GetInputPort(address);
        if (input is not ApplicationInputPort { Kind: ApplicationPortKind.Signal } signal)
        {
            throw new InvalidOperationException(
                $"Application port '{address}' is a message input, not a signal input.");
        }

        return signal;
    }

    private ApplicationOutputPort GetOutput(ApplicationAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!_outputs.TryGetValue(address, out var output))
            throw CreatePortException(address, ApplicationPortDirection.Output);
        if (output is not ApplicationOutputPort canonical)
            throw CreateTypeException(address, typeof(global::FluxFlow.Data.FlowValue), output.PayloadType);
        return canonical;
    }

    private Exception CreatePortException(
        ApplicationAddress address,
        ApplicationPortDirection expected)
    {
        if (_metadataByAddress.TryGetValue(address, out var actual))
        {
            return new InvalidOperationException(
                $"Application port '{address}' is an {actual.Direction} port, not an {expected} port.");
        }

        return new KeyNotFoundException($"Application port '{address}' is not registered.");
    }

    private static InvalidOperationException CreateTypeException(
        ApplicationAddress address,
        Type requested,
        Type actual)
        => new(
            $"Application port '{address}' carries payload type '{actual}', not requested type '{requested}'.");

    private static async Task<ApplicationOutputReceiveResult> WaitForReceiveAsync(
        ApplicationOutputReceiveRegistration registration,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        if (timeout is null || timeout == Timeout.InfiniteTimeSpan)
            return await registration.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await registration.Task
                .WaitAsync(timeout.Value, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return new ApplicationOutputReceiveResult(PortReceiveStatus.TimedOut);
        }
    }

    private static void ValidateTimeout(TimeSpan? timeout)
    {
        if (timeout is not null &&
            timeout != Timeout.InfiniteTimeSpan &&
            timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive or infinite.");
        }
    }

    private void SetState(ApplicationRuntimeState state)
    {
        lock (_statusGate)
        {
            if (_state == ApplicationRuntimeState.Disposed || _state == state)
                return;
            _state = state;
            _stateChangedAt = DateTimeOffset.UtcNow;
        }
    }

    private void RemoveLink(IDisposable link)
    {
        lock (_gate)
            _links.Remove(link);
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private sealed class RuntimeLink(
        ApplicationPortRuntime owner,
        IDisposable inner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                inner.Dispose();
                owner.RemoveLink(inner);
            }
        }
    }
    private static readonly JsonSerializerOptions ValueJsonOptions = new(JsonSerializerDefaults.Web);
}
