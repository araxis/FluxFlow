using FluxFlow.Mapping;

namespace FluxFlow.Components.Routing.Contracts;

public interface IRoutingContextFactory
{
    FlowMapContext Create(object? input, RoutingNodeContext context);
}
