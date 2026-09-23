using FluxFlow.Core.Nodes;
using Xunit;
using Shouldly;

namespace FluxFlow.Nodes.Tests;

public sealed class ComponentBoundaryTests
{
    [Theory]
    [InlineData(typeof(IFlowNode))]
    [InlineData(typeof(FlowNode))]
    [InlineData(typeof(FlowTransformNode))]
    [InlineData(typeof(FlowSource))]
    public void Component_contracts_do_not_declare_application_registration(Type type)
        => type.GetMethods().ShouldNotContain(method => method.Name == "RegisterPorts");
}
