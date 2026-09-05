using FluxFlow.Nodes;

namespace FluxFlow.Components.Observability.Nodes;

internal sealed class ObservabilityPipeline<TInput, TOutput> : FlowNode<TInput, TOutput>
{
    private readonly Func<FlowMessage<TInput>, FlowMessage<TOutput>> _process;

    public ObservabilityPipeline(
        int boundedCapacity,
        Func<FlowMessage<TInput>, FlowMessage<TOutput>> process)
        : base(CreateOptions(boundedCapacity))
    {
        ArgumentNullException.ThrowIfNull(process);
        _process = process;
    }

    protected override bool HandlesErrors => true;

    public void PublishEvent(FlowEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        EmitEvent(@event);
    }

    protected override async Task ProcessAsync(FlowMessage<TInput> message)
        => await EmitAsync(_process(message), Stopping).ConfigureAwait(false);

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
