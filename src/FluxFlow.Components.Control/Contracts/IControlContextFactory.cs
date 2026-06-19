using FluxFlow.Mapping;

namespace FluxFlow.Components.Control.Contracts;

public interface IControlContextFactory
{
    FlowMapContext Create(object? input, ControlNodeContext context);
}
