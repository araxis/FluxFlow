using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Links;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.Ports;

public sealed class ApplicationPortRuntime : IAsyncDisposable
{
    private const int RejectionCapacity = 256;

    private readonly IReadOnlyDictionary<ApplicationAddress, IApplicationInputPort> _inputs;
    private readonly IReadOnlyDictionary<ApplicationAddress, IApplicationOutputPort> _outputs;
    private readonly IReadOnlyDictionary<ApplicationAddress, ApplicationPortMetadata> _metadataByAddress;
    private readonly BufferBlock<ApplicationPortRejection> _rejections = new(
        new DataflowBlockOptions { BoundedCapacity = RejectionCapacity });
    private readonly List<IDisposable> _links = [];
    private readonly object _gate = new();
    private readonly Task _completion;
    private int _completeRequested;
    private int _disposed;

    internal ApplicationPortRuntime(
        IReadOnlyList<ApplicationPortRuntimeBuilder.PortRegistration> registrations)
    {
        var inputs = new Dictionary<ApplicationAddress, IApplicationInputPort>();
        var outputs = new Dictionary<ApplicationAddress, IApplicationOutputPort>();
        var metadata = new Dictionary<ApplicationAddress, ApplicationPortMetadata>();

        foreach (var registration in registrations)
        {
            var portMetadata = new ApplicationPortMetadata
            {
                Address = registration.Address,
                Direction = registration.Direction,
                PayloadType = registration.PayloadType,
                Capacity = registration.Capacity
            };
            metadata.Add(registration.Address, portMetadata);

            if (registration.Direction == ApplicationPortDirection.Input)
            {
                inputs.Add(
                    registration.Address,
                    registration.CreateInput!(Report));
            }
            else
            {
                outputs.Add(
                    registration.Address,
                    registration.CreateOutput!(Report));
            }
        }

        _inputs = inputs;
        _outputs = outputs;
        _metadataByAddress = metadata;
        Ports = metadata.Values
            .OrderBy(static value => value.Address.Value, StringComparer.Ordinal)
            .ToArray();
        _completion = CompleteRuntimeAsync();
    }

    public IReadOnlyList<ApplicationPortMetadata> Ports { get; }

    public ISourceBlock<ApplicationPortRejection> Rejections => _rejections;

    public Task Completion => _completion;

    public ValueTask<PortSendResult> SendAsync<T>(
        ApplicationAddress input,
        FlowMessage<T> message,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(GetInput<T>(input).TrySend(message));
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
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateTimeout(timeout);

        using var registration = GetOutput<TResponse>(output).RegisterReceive(request.TraceId);
        if (registration.Task.IsCompletedSuccessfully)
        {
            var initial = registration.Task.Result;
            if (initial.Status != PortReceiveStatus.Received)
            {
                return new PortRequestResult<TResponse>
                {
                    InputPort = input,
                    OutputPort = output,
                    Status = initial.Status == PortReceiveStatus.Completed
                        ? PortRequestStatus.OutputCompleted
                        : PortRequestStatus.OutputUnavailable
                };
            }
        }

        var send = await SendAsync(input, request, cancellationToken).ConfigureAwait(false);
        if (!send.IsAccepted)
        {
            return new PortRequestResult<TResponse>
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
        }

        var receive = await WaitForReceiveAsync(
                registration,
                output,
                timeout,
                cancellationToken)
            .ConfigureAwait(false);

        return new PortRequestResult<TResponse>
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
    }

    public ValueTask<IAsyncDisposable> AttachInputAsync<T>(
        ApplicationAddress input,
        ITargetBlock<FlowMessage<T>> target,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return GetInput<T>(input).AttachAsync(target, cancellationToken);
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
        if (output.PayloadType != input.PayloadType || output.PayloadType != link.MessageType)
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
        if (Interlocked.Exchange(ref _completeRequested, 1) != 0)
            return;

        foreach (var output in _outputs.Values)
            output.Complete();
        _ = CompleteInputsAfterOutputsAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        IDisposable[] links;
        lock (_gate)
        {
            links = _links.ToArray();
            _links.Clear();
        }

        foreach (var link in links)
            link.Dispose();
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
            _rejections.Complete();
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
        }
        finally
        {
            _rejections.Complete();
        }
    }

    private async Task CompleteInputsAfterOutputsAsync()
    {
        try
        {
            await Task.WhenAll(_outputs.Values.Select(static port => port.Completion))
                .ConfigureAwait(false);
        }
        catch
        {
            // Output faults are already represented by their Completion tasks.
            // Inputs still need an explicit terminal signal.
        }

        foreach (var input in _inputs.Values)
            input.Complete();
    }

    private ApplicationInputPort<T> GetInput<T>(ApplicationAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!_inputs.TryGetValue(address, out var input))
            throw CreatePortException(address, ApplicationPortDirection.Input);
        if (input is not ApplicationInputPort<T> typed)
            throw CreateTypeException(address, typeof(T), input.PayloadType);
        return typed;
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

    private void Report(ApplicationPortRejection rejection)
        => _rejections.Post(rejection);

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
