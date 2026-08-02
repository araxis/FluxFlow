using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Storage.Nodes;

internal sealed class StorageOperationPipeline<TInput, TOutput> : FlowNode<TInput, TOutput>
{
    private readonly Func<FlowMessage<TInput>, CancellationToken, Task<FlowMessage<TOutput>>> _process;

    public StorageOperationPipeline(
        int boundedCapacity,
        Func<FlowMessage<TInput>, CancellationToken, Task<FlowMessage<TOutput>>> process)
        : base(CreateOptions(boundedCapacity))
    {
        ArgumentNullException.ThrowIfNull(process);
        _process = process;
    }

    public void PublishEvent(FlowEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        EmitEvent(value);
    }

    protected override async Task ProcessAsync(FlowMessage<TInput> message)
    {
        var result = await _process(message, Stopping).ConfigureAwait(false);
        await EmitAsync(result, Stopping).ConfigureAwait(false);
    }

    private static FlowNodeOptions CreateOptions(int boundedCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(boundedCapacity, 1);
        return new FlowNodeOptions
        {
            InputCapacity = boundedCapacity,
            OutputCapacity = boundedCapacity
        };
    }
}
