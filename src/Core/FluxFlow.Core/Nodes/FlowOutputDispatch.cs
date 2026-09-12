namespace FluxFlow.Nodes;

/// <summary>A selected admission boundary; it owns no scheduling or queue.</summary>
internal interface IFlowOutputRoute<in T>
{
    ValueTask DeliverAsync(T value, CancellationToken cancellationToken);
}

/// <summary>A route snapshot, prepared before the output starts fanout.</summary>
internal readonly record struct FlowOutputDispatch<T>(
    IFlowOutputRoute<T>[] Routes,
    Action? Delivered = null);
