namespace FluxFlow.Nodes;

public interface IFlowSignalTarget
{
    Task Completion { get; }

    ValueTask<bool> SendAsync<T>(
        FlowMessage<T> signal,
        CancellationToken cancellationToken = default);
}
