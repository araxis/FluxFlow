using FluxFlow.Application.Addressing;
using Shouldly;
using Xunit;

namespace FluxFlow.Core.Tests;

public sealed class TypedAddressTests
{
    [Theory]
    [InlineData("main")]
    [InlineData("main.node.Input")]
    [InlineData("Resources.connection")]
    [InlineData("System.Events.Output")]
    [InlineData(" main.node")]
    public void Component_addresses_reject_other_address_shapes(string value)
        => Should.Throw<FormatException>(() => ComponentAddress.Parse(value));

    [Theory]
    [InlineData("main.node")]
    [InlineData("Resources.connection.host")]
    [InlineData("main.node.")]
    [InlineData("main..Input")]
    [InlineData("main.node.Input.extra")]
    [InlineData("main.node. Input")]
    public void Port_addresses_reject_invalid_or_non_port_addresses(string value)
        => Should.Throw<FormatException>(() => PortAddress.Parse(value));

    [Fact]
    public void Constructed_parsed_and_relative_addresses_have_value_equality()
    {
        var component = ComponentAddress.Create("main", "node");
        component.ShouldBe(ComponentAddress.Parse("main.node"));
        component.WorkflowName.ShouldBe("main");
        component.ComponentName.ShouldBe("node");
        var port = component.Port("Input");
        port.ComponentAddress.ShouldBe(component);
        port.ShouldBe(PortAddress.Parse("main.node.Input"));
        port.ShouldBe(PortAddress.Resolve("node.Input", ComponentAddress.Create("main", "other")));
        new HashSet<PortAddress> { port, PortAddress.Parse(port.Value) }.Count.ShouldBe(1);
        port.ShouldNotBe(component.Port("input"));
        port.IsSystem.ShouldBeFalse();
        port.PortName.ShouldBe("Input");
        Should.Throw<ArgumentException>(() => component.Port("bad.name"));
    }

    [Fact]
    public void Full_cross_workflow_and_system_ports_keep_their_identity()
    {
        var scope = ComponentAddress.Create("main", "node");
        PortAddress.Resolve("other.node.Output", scope).Value.ShouldBe("other.node.Output");
        var system = PortAddress.Resolve("System.Events.Output", scope);
        system.IsSystem.ShouldBeTrue();
        system.ComponentAddress.ShouldBeNull();
        system.ShouldBe(PortAddress.Parse("System.Events.Output"));
    }
}
