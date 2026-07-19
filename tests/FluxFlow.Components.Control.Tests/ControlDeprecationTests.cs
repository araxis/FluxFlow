using FluxFlow.Components.Control.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Control.Tests;

#pragma warning disable CS0618

public sealed class ControlDeprecationTests
{
    [Theory]
    [InlineData(typeof(FilterNode<>))]
    [InlineData(typeof(WhenNode<>))]
    public void Structural_control_node_points_to_canonical_link_conditions(Type nodeType)
    {
        var attribute = nodeType
            .GetCustomAttributes(typeof(ObsoleteAttribute), inherit: false)
            .ShouldHaveSingleItem()
            .ShouldBeOfType<ObsoleteAttribute>();

        attribute.Message.ShouldBe("Use canonical conditional workflow links instead.");
        attribute.IsError.ShouldBeFalse();
    }
}
