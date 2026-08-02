using FluxFlow.Composition.Addressing;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.Ports;

internal sealed class ApplicationOutputReceiveRegistration<T> : IDisposable
{
    private readonly ApplicationOutputReceiveWaiter<T>? _waiter;

    internal ApplicationOutputReceiveRegistration(ApplicationOutputReceiveWaiter<T> waiter)
    {
        _waiter = waiter;
        Task = waiter.Task;
    }

    private ApplicationOutputReceiveRegistration(PortReceiveResult<T> result)
    {
        Task = System.Threading.Tasks.Task.FromResult(result);
    }

    internal Task<PortReceiveResult<T>> Task { get; }

    public void Dispose() => _waiter?.Dispose();

    internal static ApplicationOutputReceiveRegistration<T> Completed(
        PortReceiveResult<T> result)
        => new(result);
}

internal sealed class ApplicationOutputReceiveWaiter<T>(
    ApplicationOutputPort<T> owner,
    TraceId? traceId) : IDisposable
{
    private readonly TaskCompletionSource<PortReceiveResult<T>> _result =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;

    internal Task<PortReceiveResult<T>> Task => _result.Task;

    internal void TryDeliver(FlowMessage<T> message)
    {
        if (traceId is not null && message.TraceId != traceId.Value)
            return;
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        owner.RemoveWaiter(this);
        _result.TrySetResult(new PortReceiveResult<T>
        {
            Port = owner.Address,
            Status = PortReceiveStatus.Received,
            Message = message
        });
    }

    internal void Complete()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        owner.RemoveWaiter(this);
        _result.TrySetResult(new PortReceiveResult<T>
        {
            Port = owner.Address,
            Status = PortReceiveStatus.Completed
        });
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            owner.RemoveWaiter(this);
    }
}
