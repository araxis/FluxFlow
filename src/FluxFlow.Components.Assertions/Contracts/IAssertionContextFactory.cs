using FluxFlow.Mapping;

namespace FluxFlow.Components.Assertions.Contracts;

public interface IAssertionContextFactory
{
    FlowMapContext Create(object? input, AssertionNodeContext context);
}
