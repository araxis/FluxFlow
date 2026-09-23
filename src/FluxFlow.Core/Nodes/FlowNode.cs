using FluxFlow.Core.Messaging;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Core.Nodes;

/// <summary>Explicit-emission authoring over the shared native TransformMany execution.</summary>
public abstract class FlowNode : FlowTransformNode
{
    protected FlowNode(ExecutionDataflowBlockOptions? options = null, int outputCapacity = 128)
        : base(options, outputCapacity) { }

    /// <summary>Processes one message; await EmitAsync for immediate, backpressured output.</summary>
    protected abstract Task ProcessAsync(FlowMessage message);
    private protected sealed override bool EmitsDirectly => true;

    protected sealed override async Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
    {
        await ProcessAsync(message).ConfigureAwait(false);
        return [];
    }

    /// <summary>Flushes retained work after input drains. Not called on fault.</summary>
    protected virtual ValueTask OnInputCompletedAsync() => ValueTask.CompletedTask;

    protected sealed override async ValueTask<IEnumerable<FlowMessage>> FlushAsync()
    {
        await OnInputCompletedAsync().ConfigureAwait(false);
        return [];
    }
}
