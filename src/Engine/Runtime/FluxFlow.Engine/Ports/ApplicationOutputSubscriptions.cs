using FluxFlow.Nodes;

namespace FluxFlow.Engine.Ports;

internal readonly record struct ApplicationOutputReceiveResult(PortReceiveStatus Status, FlowMessage? Message = null);

internal sealed class ApplicationOutputReceiveRegistration : IDisposable
{
    private readonly ApplicationOutputReceiveWaiter? _waiter;

    internal ApplicationOutputReceiveRegistration(ApplicationOutputReceiveWaiter waiter)
    {
        _waiter = waiter;
        Task = waiter.Task;
    }

    private ApplicationOutputReceiveRegistration(ApplicationOutputReceiveResult result)
    {
        Task = System.Threading.Tasks.Task.FromResult(result);
    }

    internal Task<ApplicationOutputReceiveResult> Task { get; }

    public void Dispose() => _waiter?.Dispose();

    internal static ApplicationOutputReceiveRegistration Completed(
        ApplicationOutputReceiveResult result)
        => new(result);
}

internal sealed class ApplicationOutputReceiveWaiter(
    ApplicationOutputPort owner) : IDisposable
{
    private readonly TaskCompletionSource<ApplicationOutputReceiveResult> _result =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;

    internal Task<ApplicationOutputReceiveResult> Task => _result.Task;

    internal void TryDeliver(FlowMessage message)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        owner.RemoveWaiter(this);
        _result.TrySetResult(new ApplicationOutputReceiveResult(PortReceiveStatus.Received, message));
    }

    internal void Complete()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        owner.RemoveWaiter(this);
        _result.TrySetResult(new ApplicationOutputReceiveResult(PortReceiveStatus.Completed));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            owner.RemoveWaiter(this);
    }
}
