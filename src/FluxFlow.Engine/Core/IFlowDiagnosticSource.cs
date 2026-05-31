using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Engine.Components;

public interface IFlowDiagnosticSource
{
    ISourceBlock<FlowDiagnostic> Diagnostics { get; }
}
