using FluxFlow.Data;

namespace FluxFlow.Components.Observability.Contracts;

public interface IObservabilityValueSelector
{
    FlowValue Select(FlowValue input, ObservabilityNodeContext context);
}
