using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Engine.Components;

public interface IFlowNode : IDataflowBlock
{
    FlowNodeId Id { get; }
    ISourceBlock<FlowError> Errors { get; }
    Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
