using FluxFlow.Composition.Addressing;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.Ports;

internal sealed class FlowValueApplicationOutputReceiveRegistration : IDisposable
{
    private readonly FlowValueApplicationOutputReceiveWaiter? _waiter;

    internal FlowValueApplicationOutputReceiveRegistration(FlowValueApplicationOutputReceiveWaiter waiter)
    {
        _waiter = waiter;
        Task = waiter.Task;
    }

    private FlowValueApplicationOutputReceiveRegistration(PortReceiveResult<global::FluxFlow.Data.FlowValue> result)
    {
        Task = System.Threading.Tasks.Task.FromResult(result);
    }

    internal Task<PortReceiveResult<global::FluxFlow.Data.FlowValue>> Task { get; }

    public void Dispose() => _waiter?.Dispose();

    internal static FlowValueApplicationOutputReceiveRegistration Completed(
        PortReceiveResult<global::FluxFlow.Data.FlowValue> result)
        => new(result);
}

internal sealed class FlowValueApplicationOutputReceiveWaiter(
    FlowValueApplicationOutputPort owner,
    TraceId? traceId) : IDisposable
{
    private readonly TaskCompletionSource<PortReceiveResult<global::FluxFlow.Data.FlowValue>> _result =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;

    internal Task<PortReceiveResult<global::FluxFlow.Data.FlowValue>> Task => _result.Task;

    internal void TryDeliver(FlowMessage message)
    {
        if (traceId is not null && message.TraceId != traceId.Value)
            return;
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        owner.RemoveWaiter(this);
        _result.TrySetResult(new PortReceiveResult<global::FluxFlow.Data.FlowValue>
        {
            Port = owner.Address,
            Status = PortReceiveStatus.Received,
            Message = FlowValueApplicationOutputPort.ToTypedMessage(message)
        });
    }

    internal void Complete()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        owner.RemoveWaiter(this);
        _result.TrySetResult(new PortReceiveResult<global::FluxFlow.Data.FlowValue>
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
