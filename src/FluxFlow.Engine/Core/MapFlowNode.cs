using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Engine.Components;

public abstract class MapFlowNode<TInput, TOutput> : TransformFlowNode<TInput, TOutput>
{
    protected MapFlowNode(
        ExecutionDataflowBlockOptions? inputOptions = null,
        DataflowBlockOptions? outputOptions = null)
        : base(inputOptions, outputOptions)
    {
    }

    protected MapFlowNode(
        FlowNodeId id,
        ExecutionDataflowBlockOptions? inputOptions = null,
        DataflowBlockOptions? outputOptions = null)
        : base(id, inputOptions, outputOptions)
    {
    }

    protected abstract ValueTask<TOutput> MapAsync(
        TInput input,
        CancellationToken cancellationToken);

    protected sealed override async ValueTask<IReadOnlyCollection<TOutput>> TransformAsync(
        TInput input,
        CancellationToken cancellationToken)
        => [await MapAsync(input, cancellationToken).ConfigureAwait(false)];
}
