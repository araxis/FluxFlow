using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Sessions.Nodes;

internal sealed class SessionOperationPipeline<TInput, TOutput> : FlowNode<TInput, TOutput>
{
    private readonly Func<FlowMessage<TInput>, CancellationToken, Task<FlowMessage<TOutput>>> _process;
    private readonly Func<Task>? _finalize;

    public SessionOperationPipeline(
        int boundedCapacity,
        Func<FlowMessage<TInput>, CancellationToken, Task<FlowMessage<TOutput>>> process,
        Func<Task>? finalize = null)
        : base(CreateOptions(boundedCapacity))
    {
        ArgumentNullException.ThrowIfNull(process);

        _process = process;
        _finalize = finalize;
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

    protected override ValueTask OnInputCompletedAsync()
        => _finalize is null
            ? ValueTask.CompletedTask
            : new ValueTask(_finalize());

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
