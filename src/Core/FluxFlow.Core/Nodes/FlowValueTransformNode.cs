using FluxFlow.Data;

namespace FluxFlow.Nodes;

/// <summary>
/// One-result processing for components that only need the payload. The existing
/// transform runtime owns envelope identity, error forwarding, tracing and delivery.
/// </summary>
public abstract class FlowValueTransformNode : FlowTransformNode
{
    protected FlowValueTransformNode(FlowNodeOptions? options = null, bool ensureOrdered = true)
        : base(options, ensureOrdered) { }

    /// <summary>Convert locally and return a value. Use JsonFlowValue.Null for JSON null.</summary>
    protected abstract ValueTask<FlowValue> ProcessAsync(FlowValue input, CancellationToken cancellationToken);

    protected sealed override async Task<FlowMessage> TransformAsync(FlowMessage message)
    {
        var result = await ProcessAsync(message.Value!, Stopping).ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(result);
        return message.With(result);
    }
}
