using FluxFlow.Data;

namespace FluxFlow.Components.Observability.Contracts;

public interface IObservabilityFlowValueSelector
{
    FlowValue Select(FlowValue input, ObservabilityNodeContext context);
}
