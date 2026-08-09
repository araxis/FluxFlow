using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Authoring;
using FluxFlow.Engine.Ports;
using FluxFlow.Engine.Signals;
using FluxFlow.Nodes;

namespace FluxFlow.Engine;

public sealed class ApplicationPorts
{
    private readonly Func<ApplicationPortRuntime> _getRuntime;

    internal ApplicationPorts(Func<ApplicationPortRuntime> getRuntime)
    {
        _getRuntime = getRuntime ?? throw new ArgumentNullException(nameof(getRuntime));
    }

    public IReadOnlyList<ApplicationPortMetadata> Metadata => Runtime.Ports;

    public ApplicationPortRevisionInfo? CurrentRevision => Runtime.CurrentRevision;

    public ApplicationRuntimeStatus Status => Runtime.Status;

    public ISourceBlock<ApplicationPortRejection> Rejections => Runtime.Rejections;

    public ISourceBlock<FlowMessage<ApplicationSystemEvent>> SystemEvents => Runtime.SystemEvents;

    public ISourceBlock<FlowMessage<ApplicationDiagnostic>> Diagnostics => Runtime.Diagnostics;

    public Task Completion => Runtime.Completion;

    public ValueTask<PortSendResult> SendAsync<T>(
        string input,
        FlowMessage<T> message,
        CancellationToken cancellationToken = default)
        => SendAsync(ApplicationAddress.Parse(input), message, cancellationToken);

    public ValueTask<PortSendResult> SendAsync<T>(
        ApplicationAddress input,
        FlowMessage<T> message,
        CancellationToken cancellationToken = default)
        => Runtime.SendAsync(input, message, cancellationToken);

    public ValueTask<PortSendResult> SendAsync<T>(
        InputPortHandle<T> input,
        FlowMessage<T> message,
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

    public Task<PortReceiveResult<T>> ReceiveAsync<T>(
        string output,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        => ReceiveAsync<T>(ApplicationAddress.Parse(output), timeout, cancellationToken);

    public Task<PortReceiveResult<T>> ReceiveAsync<T>(
        ApplicationAddress output,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        => Runtime.ReceiveAsync<T>(output, timeout, cancellationToken);

    public Task<PortReceiveResult<T>> ReceiveAsync<T>(
        OutputPortHandle<T> output,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        return ReceiveAsync<T>(output.Address, timeout, cancellationToken);
    }

    public ValueTask<PortObserveResult<T>> ObserveAsync<T>(
        string output,
        int capacity = 128,
        CancellationToken cancellationToken = default)
        => ObserveAsync<T>(ApplicationAddress.Parse(output), capacity, cancellationToken);

    public ValueTask<PortObserveResult<T>> ObserveAsync<T>(
        ApplicationAddress output,
        int capacity = 128,
        CancellationToken cancellationToken = default)
        => Runtime.ObserveAsync<T>(output, capacity, cancellationToken);

    public ValueTask<PortObserveResult<T>> ObserveAsync<T>(
        OutputPortHandle<T> output,
        int capacity = 128,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        return ObserveAsync<T>(output.Address, capacity, cancellationToken);
    }

    public Task<PortRequestResult<TResponse>> SendAndReceiveAsync<TRequest, TResponse>(
        string input,
        string output,
        FlowMessage<TRequest> request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        => SendAndReceiveAsync<TRequest, TResponse>(
            ApplicationAddress.Parse(input),
            ApplicationAddress.Parse(output),
            request,
            timeout,
            cancellationToken);

    public Task<PortRequestResult<TResponse>> SendAndReceiveAsync<TRequest, TResponse>(
        ApplicationAddress input,
        ApplicationAddress output,
        FlowMessage<TRequest> request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        => Runtime.SendAndReceiveAsync<TRequest, TResponse>(
            input,
            output,
            request,
            timeout,
            cancellationToken);

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

    private ApplicationPortRuntime Runtime => _getRuntime();
}
