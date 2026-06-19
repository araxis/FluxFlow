using FluxFlow.Mapping;

namespace FluxFlow.Components.Observability.Contracts;

public interface IObservabilityContextFactory
{
    FlowMapContext Create(object? input, ObservabilityNodeContext context);
}
