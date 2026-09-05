namespace FluxFlow.Nodes;

public interface IFlowSignalTarget
{
    Task Completion { get; }

    ValueTask<bool> SendAsync(
        FlowMessage signal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return signal.IsError
            ? SendAsync(signal.ConvertError<global::FluxFlow.Data.FlowValue>(), cancellationToken)
            : SendAsync(signal.ConvertValue(signal.Value!), cancellationToken);
    }

    ValueTask<bool> SendAsync<T>(
        FlowMessage<T> signal,
        CancellationToken cancellationToken = default);
}
