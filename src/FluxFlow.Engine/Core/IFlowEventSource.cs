using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Engine.Components;

public interface IFlowEventSource
{
    ISourceBlock<FlowEvent> Events { get; }
}
