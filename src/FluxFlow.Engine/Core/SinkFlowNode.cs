using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Engine.Components;

public abstract class SinkFlowNode<TInput> : FlowNodeBase
{
    private readonly ActionBlock<TInput> _input;
    private readonly CancellationToken _processingCancellationToken;

    protected SinkFlowNode(ExecutionDataflowBlockOptions? inputOptions = null)
        : this(FlowNodeId.New(), inputOptions)
    {
    }

    protected SinkFlowNode(FlowNodeId id, ExecutionDataflowBlockOptions? inputOptions = null)
        : base(id)
    {
        var options = inputOptions ?? new ExecutionDataflowBlockOptions();
        _processingCancellationToken = options.CancellationToken;
        _input = new ActionBlock<TInput>(HandleInputAsync, options);
        CompleteWhen(_input.Completion);
    }

    public ITargetBlock<TInput> Input => _input;

    protected ITargetBlock<TInput> InputBlock => Input;

    protected abstract ValueTask HandleAsync(
        TInput input,
        CancellationToken cancellationToken);

    public override void Complete()
        => _input.Complete();

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            FaultNode(exception);
        }
        finally
        {
            ((IDataflowBlock)_input).Fault(exception);
        }
    }

    private async Task HandleInputAsync(TInput input)
    {
        try
        {
            await HandleAsync(input, _processingCancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_processingCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            TryReportError(
                FlowErrorCodes.ProcessingFailed,
                $"Node '{Id}' failed to process input: {exception.Message}",
                exception);
        }
    }
}
