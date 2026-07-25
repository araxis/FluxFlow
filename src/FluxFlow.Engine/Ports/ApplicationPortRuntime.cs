using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Links;
using FluxFlow.Composition.Revisions;
using FluxFlow.Engine.Signals;
using FluxFlow.Nodes;
using Microsoft.Extensions.Logging;

namespace FluxFlow.Engine.Ports;

public sealed class ApplicationPortRuntime : IApplicationRevisionEventSink, IAsyncDisposable
{
    private const int RejectionCapacity = 256;

    private readonly IReadOnlyDictionary<ApplicationAddress, IApplicationInputPort> _inputs;
    private readonly IReadOnlyDictionary<ApplicationAddress, IApplicationOutputPort> _outputs;
    private readonly IReadOnlyDictionary<ApplicationAddress, ApplicationPortMetadata> _metadataByAddress;
    private readonly ApplicationRevisionRouting _revisionRouting = new();
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
        ILogger? logger)
    {
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
                        _revisionRouting));
            }
        }

        _inputs = inputs;
        _outputs = outputs;
        GetOutput<ApplicationSystemEvent>(ApplicationAddress.SystemEvents)
            .Attach(_signals.SystemEvents);
        GetOutput<ApplicationDiagnostic>(ApplicationAddress.SystemDiagnostics)
            .Attach(_signals.Diagnostics);
        _completion = CompleteRuntimeAsync();
    }

    public IReadOnlyList<ApplicationPortMetadata> Ports { get; }

    public ISourceBlock<ApplicationPortRejection> Rejections => _rejections;

    public ISourceBlock<FlowMessage<ApplicationSystemEvent>> SystemEvents => _signals.SystemEvents;

    public ISourceBlock<FlowMessage<ApplicationDiagnostic>> Diagnostics => _signals.Diagnostics;

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

    public ValueTask<SystemEventPublishResult> PublishSystemEventAsync(
        FlowMessage<ApplicationSystemEvent> message,
        CancellationToken cancellationToken = default)
        => _signals.PublishSystemEventAsync(message, cancellationToken);

    public async ValueTask<bool> PublishAsync(
        ApplicationRevisionEvent revisionEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revisionEvent);
        var details = FlowValue.FromObject(new Dictionary<string, FlowValue>(StringComparer.Ordinal)
        {
            ["phase"] = FlowValue.From(revisionEvent.Phase.ToString()),
            ["resources"] = FlowValue.FromArray(
                revisionEvent.Resources.Select(static resource => FlowValue.From(resource.Value))),
            ["sequence"] = FlowValue.From(revisionEvent.Sequence),
            ["workflows"] = FlowValue.FromArray(
                revisionEvent.Workflows.Select(FlowValue.From))
        });
        var result = await PublishSystemEventAsync(
                FlowMessage.Create(new ApplicationSystemEvent
                {
                    Timestamp = revisionEvent.Timestamp,
                    Name = ApplicationSystemEventNames.RevisionChanged,
                    Category = ApplicationSystemEventCategory.Revision,
                    Subject = revisionEvent.RevisionId,
                    Error = revisionEvent.Error,
                    Details = details
                }),
                cancellationToken)
            .ConfigureAwait(false);
        return result.IsAccepted;
    }

    public bool TryPublishDiagnostic(FlowMessage<ApplicationDiagnostic> message)
        => _signals.TryPublishDiagnostic(message);

    public ApplicationPortRevisionBuilder CreateRevision(string revisionId)
    {
        ThrowIfDisposed();
        return new ApplicationPortRevisionBuilder(this, revisionId);
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
            _events.ReportRejection(new ApplicationPortRejection
            {
                Timestamp = DateTimeOffset.UtcNow,
                Port = input,
                CorrelationId = message.CorrelationId,
                TraceId = message.TraceId,
                MessageId = message.MessageId,
                Reason = ApplicationPortRejectionReason.Completed
            });
            return ValueTask.FromResult(new PortSendResult
            {
                Port = inputPort.Address,
                Status = PortSendStatus.Completed
            });
        }

        return ValueTask.FromResult(inputPort is IApplicationSignalInputPort signalInput
            ? signalInput.TrySend(message)
            : GetInput<T>(input).TrySend(message));
    }

    public async Task<PortReceiveResult<T>> ReceiveAsync<T>(
        ApplicationAddress output,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateTimeout(timeout);

        using var registration = GetOutput<T>(output).RegisterReceive(traceId: null);
        return await WaitForReceiveAsync(
                registration,
                output,
                timeout,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public ValueTask<PortObserveResult<T>> ObserveAsync<T>(
        ApplicationAddress output,
        int capacity = 128,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(GetOutput<T>(output).Observe(capacity));
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

        using var registration = GetOutput<TResponse>(output).RegisterReceive(request.TraceId);
        if (registration.Task.IsCompletedSuccessfully)
        {
            var initial = registration.Task.Result;
            if (initial.Status != PortReceiveStatus.Received)
            {
                var unavailable = new PortRequestResult<TResponse>
                {
                    InputPort = input,
                    OutputPort = output,
                    Status = initial.Status == PortReceiveStatus.Completed
                        ? PortRequestStatus.OutputCompleted
                        : PortRequestStatus.OutputUnavailable
                };
                _events.ReportRequest(unavailable.Status, request, input, output, startedAt);
                return unavailable;
            }
        }

        var send = await SendAsync(input, request, cancellationToken).ConfigureAwait(false);
        if (!send.IsAccepted)
        {
            var rejected = new PortRequestResult<TResponse>
            {
                InputPort = input,
                OutputPort = output,
                Status = send.Status switch
                {
                    PortSendStatus.Full => PortRequestStatus.InputFull,
                    PortSendStatus.Unavailable => PortRequestStatus.InputUnavailable,
                    PortSendStatus.Completed => PortRequestStatus.InputCompleted,
                    _ => throw new ArgumentOutOfRangeException(nameof(send.Status))
                }
            };
            _events.ReportRequest(rejected.Status, request, input, output, startedAt);
            return rejected;
        }

        var receive = await WaitForReceiveAsync(
                registration,
                output,
                timeout,
                cancellationToken)
            .ConfigureAwait(false);

        var result = new PortRequestResult<TResponse>
        {
            InputPort = input,
            OutputPort = output,
            Status = receive.Status switch
            {
                PortReceiveStatus.Received => PortRequestStatus.Received,
                PortReceiveStatus.Completed => PortRequestStatus.OutputCompleted,
                PortReceiveStatus.Unavailable => PortRequestStatus.OutputUnavailable,
                PortReceiveStatus.TimedOut => PortRequestStatus.TimedOut,
                _ => throw new ArgumentOutOfRangeException(nameof(receive.Status))
            },
            Response = receive.Message
        };
        _events.ReportRequest(result.Status, request, input, output, startedAt);
        return result;
    }

    public ValueTask<IAsyncDisposable> AttachInputAsync<T>(
        ApplicationAddress input,
        ITargetBlock<FlowMessage<T>> target,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return GetInput<T>(input).AttachAsync(target, cancellationToken);
    }

    public ValueTask<IAsyncDisposable> AttachSignalInputAsync(
        ApplicationAddress input,
        IFlowSignalTarget target,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(target);
        return GetSignalInput(input).AttachAsync(target, cancellationToken);
    }

    public IFlowSignalTarget GetSignalTarget(ApplicationAddress input)
    {
        ThrowIfDisposed();
        return GetSignalInput(input);
    }

    public IDisposable AttachOutput<T>(
        ApplicationAddress output,
        ISourceBlock<FlowMessage<T>> source)
    {
        ThrowIfDisposed();
        return GetOutput<T>(output).Attach(source);
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

        var connection = output.Connect(input, link);
        lock (_gate)
            _links.Add(connection);
        return new RuntimeLink(this, connection);
    }

    public void Complete()
    {
        lock (_gate)
        {
            if (Interlocked.Exchange(ref _completeRequested, 1) != 0)
                return;
        }

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

    internal void ValidateRevisionInput<T>(ApplicationAddress address)
        => _ = GetInput<T>(address);

    internal void ValidateRevisionSignalInput(ApplicationAddress address)
        => _ = GetSignalInput(address);

    internal IPreparedApplicationOutput PrepareRevisionOutput<T>(
        ApplicationAddress address,
        ISourceBlock<FlowMessage<T>> source)
        => GetOutput<T>(address).PrepareRevisionOutput(source);

    internal ApplicationRevisionRouting.Snapshot PrepareRevisionRouting(
        IEnumerable<CompiledApplicationLink> links)
    {
        var routes = new Dictionary<ApplicationAddress, List<IApplicationRevisionRoute>>();
        var identities = new HashSet<ApplicationRevisionRouting.RouteIdentity>();
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
                link.ConditionExpression);
            if (!identities.Add(identity))
                throw new InvalidOperationException($"Revision contains duplicate link '{link.Source}' to '{link.Target}'.");
            if (!routes.TryGetValue(link.Source, out var values))
            {
                values = [];
                routes.Add(link.Source, values);
            }

            values.Add(output.CreateRevisionRoute(input, link));
        }

        return new ApplicationRevisionRouting.Snapshot(
            routes.ToDictionary(
                static pair => pair.Key,
                static pair => (IReadOnlyList<IApplicationRevisionRoute>)pair.Value.ToArray()),
            identities);
    }

    internal async ValueTask<ApplicationPortRevisionLease> ActivateRevisionAsync(
        ApplicationPortRevision revision,
        CancellationToken cancellationToken)
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

            cancellationToken.ThrowIfCancellationRequested();
            foreach (var output in revision.PreparedOutputs)
                output.ThrowIfFaulted();
            foreach (var output in revision.PreparedOutputs)
                output.Activate();

            var inputAttachments = new List<IApplicationInputAttachment>();
            ApplicationPortRevisionInfo info;
            lock (_gate)
            {
                ThrowIfDisposed();
                if (Volatile.Read(ref _completeRequested) != 0)
                    throw new InvalidOperationException("The application port runtime is completing.");

                foreach (var inputRevision in inputRevisions)
                {
                    revision.InputReplacements.TryGetValue(inputRevision.Address, out var target);
                    if (revision.InputReplacements.ContainsKey(inputRevision.Address))
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
            return new ApplicationPortRevisionLease(info, inputAttachments, outputs);
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
                    FlowMessage.Create(new ApplicationSystemEvent
                    {
                        Timestamp = DateTimeOffset.UtcNow,
                        Name = ApplicationSystemEventNames.RuntimeCompleting,
                        Category = ApplicationSystemEventCategory.Lifecycle,
                        Subject = "runtime"
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

    private ApplicationInputPort<T> GetInput<T>(ApplicationAddress address)
    {
        var input = GetInputPort(address);
        if (input is not ApplicationInputPort<T> typed)
            throw CreateTypeException(address, typeof(T), input.PayloadType);
        return typed;
    }

    private IApplicationInputPort GetInputPort(ApplicationAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!_inputs.TryGetValue(address, out var input))
            throw CreatePortException(address, ApplicationPortDirection.Input);
        return input;
    }

    private IApplicationSignalInputPort GetSignalInput(ApplicationAddress address)
    {
        var input = GetInputPort(address);
        if (input is not IApplicationSignalInputPort signal)
        {
            throw new InvalidOperationException(
                $"Application port '{address}' is a message input, not a signal input.");
        }

        return signal;
    }

    private ApplicationOutputPort<T> GetOutput<T>(ApplicationAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!_outputs.TryGetValue(address, out var output))
            throw CreatePortException(address, ApplicationPortDirection.Output);
        if (output is not ApplicationOutputPort<T> typed)
            throw CreateTypeException(address, typeof(T), output.PayloadType);
        return typed;
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

    private static async Task<PortReceiveResult<T>> WaitForReceiveAsync<T>(
        ApplicationOutputPort<T>.ReceiveRegistration registration,
        ApplicationAddress output,
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
            return new PortReceiveResult<T>
            {
                Port = output,
                Status = PortReceiveStatus.TimedOut
            };
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
}
