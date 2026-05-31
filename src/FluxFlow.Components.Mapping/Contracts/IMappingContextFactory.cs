using FluxFlow.Engine.Mapping;

namespace FluxFlow.Components.Mapping.Contracts;

public interface IMappingContextFactory
{
    FlowMapContext Create(object? input, MappingNodeContext context);
}
