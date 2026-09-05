using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Links;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.Ports;

internal interface IApplicationOutputLink<T> : IDisposable
{
    void TryDeliver(FlowMessage<T> message);
}

internal sealed class ApplicationMessageOutputLink<T>(
    ApplicationOutputPort<T> owner,
    ApplicationInputPort<T> target,
    CompiledApplicationLink link) : IApplicationOutputLink<T>
{
    private int _disposed;

    public void TryDeliver(FlowMessage<T> message)
    {
        if (Volatile.Read(ref _disposed) == 0)
            owner.TryDeliver(target, link, message);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            owner.RemoveLink(this);
    }
}

internal sealed class ApplicationSignalOutputLink<T>(
    ApplicationOutputPort<T> owner,
    IApplicationSignalInputPort target,
    CompiledApplicationLink link) : IApplicationOutputLink<T>
{
    private int _disposed;

    public void TryDeliver(FlowMessage<T> message)
    {
        if (Volatile.Read(ref _disposed) == 0)
            owner.TryDeliver(target, link, message);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            owner.RemoveLink(this);
    }
}

internal sealed class ApplicationMessageRevisionRoute<T>(
    ApplicationOutputPort<T> owner,
    ApplicationInputPort<T> target,
    CompiledApplicationLink link) : IApplicationRevisionRoute
{
    public ApplicationAddress Source => link.Source;

    public ApplicationAddress Target => link.Target;

    public void TryDeliver(object message)
        => owner.TryDeliver(target, link, (FlowMessage<T>)message);
}

internal sealed class ApplicationSignalRevisionRoute<T>(
    ApplicationOutputPort<T> owner,
    IApplicationSignalInputPort target,
    CompiledApplicationLink link) : IApplicationRevisionRoute
{
    public ApplicationAddress Source => link.Source;

    public ApplicationAddress Target => link.Target;

    public void TryDeliver(object message)
        => owner.TryDeliver(target, link, (FlowMessage<T>)message);
}
