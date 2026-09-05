using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Links;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.Ports;

internal interface IFlowValueApplicationOutputLink : IDisposable
{
    void TryDeliver(FlowMessage message);
}

internal sealed class FlowValueApplicationMessageOutputLink(
    FlowValueApplicationOutputPort owner,
    FlowValueApplicationInputPort target,
    CompiledApplicationLink link) : IFlowValueApplicationOutputLink
{
    private int _disposed;

    public void TryDeliver(FlowMessage message)
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

internal sealed class FlowValueApplicationSignalOutputLink(
    FlowValueApplicationOutputPort owner,
    IApplicationSignalInputPort target,
    CompiledApplicationLink link) : IFlowValueApplicationOutputLink
{
    private int _disposed;

    public void TryDeliver(FlowMessage message)
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

internal sealed class FlowValueApplicationMessageRevisionRoute(
    FlowValueApplicationOutputPort owner,
    FlowValueApplicationInputPort target,
    CompiledApplicationLink link) : IApplicationRevisionRoute
{
    public ApplicationAddress Source => link.Source;

    public ApplicationAddress Target => link.Target;

    public void TryDeliver(object message)
        => owner.TryDeliver(target, link, (FlowMessage)message);
}

internal sealed class FlowValueApplicationSignalRevisionRoute(
    FlowValueApplicationOutputPort owner,
    IApplicationSignalInputPort target,
    CompiledApplicationLink link) : IApplicationRevisionRoute
{
    public ApplicationAddress Source => link.Source;

    public ApplicationAddress Target => link.Target;

    public void TryDeliver(object message)
        => owner.TryDeliver(target, link, (FlowMessage)message);
}
