using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.FileSystem.Nodes;

internal sealed class FileSystemOperationPipeline<TInput, TOutput> : FlowNode<TInput, TOutput>
{
    private readonly Func<FlowMessage<TInput>, CancellationToken, Task<FlowMessage<TOutput>>> _process;

    public FileSystemOperationPipeline(
        int boundedCapacity,
        Func<FlowMessage<TInput>, CancellationToken, Task<FlowMessage<TOutput>>> process)
        : base(CreateOptions(boundedCapacity))
    {
        ArgumentNullException.ThrowIfNull(process);
        _process = process;
    }

    public void PublishEvent(FlowEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        EmitEvent(@event);
    }

    protected override async Task ProcessAsync(FlowMessage<TInput> message)
    {
        var result = await _process(message, Stopping).ConfigureAwait(false);
        Emit(result);
    }

    private static FlowNodeOptions CreateOptions(int boundedCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(boundedCapacity, 1);
        return new FlowNodeOptions { InputCapacity = boundedCapacity };
    }
}
