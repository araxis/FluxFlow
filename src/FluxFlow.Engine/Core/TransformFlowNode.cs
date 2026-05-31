using FluxFlow.Engine.Core;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Engine.Components;

public abstract class TransformFlowNode<TInput, TOutput> : FlowNodeBase
{
    private readonly TransformManyBlock<TInput, TOutput> _transform;
    private readonly BufferBlock<TOutput> _output;
    private readonly CancellationToken _processingCancellationToken;

    protected TransformFlowNode(
        ExecutionDataflowBlockOptions? inputOptions = null,
        DataflowBlockOptions? outputOptions = null)
        : this(FlowNodeId.New(), inputOptions, outputOptions)
    {
    }

    protected TransformFlowNode(
        FlowNodeId id,
        ExecutionDataflowBlockOptions? inputOptions = null,
        DataflowBlockOptions? outputOptions = null)
        : base(id)
    {
        var options = inputOptions ?? new ExecutionDataflowBlockOptions();
        _processingCancellationToken = options.CancellationToken;
        _transform = new TransformManyBlock<TInput, TOutput>(TransformInputAsync, options);
        _output = new BufferBlock<TOutput>(outputOptions ?? new DataflowBlockOptions());
        _transform.LinkTo(
            _output,
            new DataflowLinkOptions { PropagateCompletion = true });
        CompleteWhen(_output.Completion);
    }

    public ITargetBlock<TInput> Input => _transform;

    public ISourceBlock<TOutput> Output => _output;

    protected ITargetBlock<TInput> InputBlock => Input;

    protected ISourceBlock<TOutput> OutputBlock => Output;

    protected abstract ValueTask<IReadOnlyCollection<TOutput>> TransformAsync(
        TInput input,
        CancellationToken cancellationToken);

    public override void Complete()
        => _transform.Complete();

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            FaultNode(exception);
        }
        finally
        {
            ((IDataflowBlock)_transform).Fault(exception);
        }
    }

    private async Task<IEnumerable<TOutput>> TransformInputAsync(TInput input)
    {
        try
        {
            return await TransformAsync(input, _processingCancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_processingCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            TryReportError(
                FlowErrorCodes.ProcessingFailed,
                $"Node '{Id}' failed to transform input: {exception.Message}",
                exception);
            return [];
        }
    }
}
