using FluxFlow.Engine.Core;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Engine.Components;

public abstract class SourceFlowNode<TOutput> : FlowNodeBase
{
    private readonly BufferBlock<TOutput> _output;

    protected SourceFlowNode(DataflowBlockOptions? outputOptions = null)
        : this(FlowNodeId.New(), outputOptions)
    {
    }

    protected SourceFlowNode(FlowNodeId id, DataflowBlockOptions? outputOptions = null)
        : base(id)
    {
        _output = new BufferBlock<TOutput>(outputOptions ?? new DataflowBlockOptions());
        CompleteWhen(_output.Completion);
    }

    protected ISourceBlock<TOutput> OutputBlock => _output;

    protected bool PostOutput(TOutput output)
        => _output.Post(output);

    protected Task<bool> SendOutputAsync(
        TOutput output,
        CancellationToken cancellationToken = default)
        => _output.SendAsync(output, cancellationToken);

    protected void CompleteOutput()
        => _output.Complete();

    public override void Complete()
        => _output.Complete();

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ((IDataflowBlock)_output).Fault(exception);
    }
}
