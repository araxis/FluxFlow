namespace FluxFlow.Nodes;

/// <summary>Lifecycle shared by every node: complete it, fault it, await it, dispose it.</summary>
public interface IFlowNode : IAsyncDisposable
{
    Task Completion { get; }

    void Complete();

    void Fault(Exception exception);
}

/// <summary>A node that must be started to begin producing (timers, watchers, triggers).</summary>
public interface IFlowSource : IFlowNode
{
    Task StartAsync(CancellationToken cancellationToken = default);
}
