using FluxFlow.Application.Addressing;
using Shouldly;
using Xunit;

namespace FluxFlow.Core.Tests;

public sealed class ApplicationAddressTests
{
    [Theory]
    [InlineData("Orders.Normalize", "Orders", "Normalize", null, "Orders.Normalize")]
    [InlineData("Orders.Normalize.Input", "Orders", "Normalize", "Input", "Orders.Normalize")]
    [InlineData("orders.Normalize.Output", "orders", "Normalize", "Output", "orders.Normalize")]
    [InlineData("Resources.Connections.Primary", null, null, null, null)]
    [InlineData("Orders.Resources.Connections.Primary", "Orders", null, null, null)]
    [InlineData("System.Events.Output", null, null, "Output", null)]
    [InlineData("System.Diagnostics.Output", null, null, "Output", null)]
    public void Address_parts_respect_workflow_component_resource_and_system_boundaries(
        string value, string? workflow, string? component, string? port, string? componentAddress)
    {
        var address = ApplicationAddress.Parse(value);
        address.WorkflowName.ShouldBe(workflow);
        address.ComponentName.ShouldBe(component);
        address.PortName.ShouldBe(port);
        (address.ComponentAddress?.Value).ShouldBe(componentAddress);
        address.Value.ShouldBe(value);
        if (address.Kind == ApplicationAddressKind.WorkflowComponent)
            address.ComponentAddress.ShouldBeSameAs(address);
    }

    [Fact]
    public void ParsesNestedResourcesAndAbsoluteWorkflowComponentsAndPorts()
    {
        var resource = ApplicationAddress.Parse("Resources.Messaging.Client1");
        var component = ApplicationAddress.Parse("OrderProcessing.ValidateOrder");
        var port = ApplicationAddress.Parse("OrderProcessing.ValidateOrder.Input");

        resource.Kind.ShouldBe(ApplicationAddressKind.Resource);
        resource.Segments.ShouldBe(["Resources", "Messaging", "Client1"]);
        resource.Value.ShouldBe("Resources.Messaging.Client1");
        component.Kind.ShouldBe(ApplicationAddressKind.WorkflowComponent);
        component.ShouldBe(ApplicationAddress.WorkflowComponent(
            "OrderProcessing",
            "ValidateOrder"));
        port.Kind.ShouldBe(ApplicationAddressKind.WorkflowPort);
        port.Segments.ShouldBe(["OrderProcessing", "ValidateOrder", "Input"]);
    }

    [Fact]
    public void ResolvesLocalAndAbsolutePortReferencesThroughOneAddressType()
    {
        ApplicationAddress.ResolvePort("ValidateOrder.Input", "OrderProcessing").ComponentAddress
            .ShouldBe(ApplicationAddress.Parse("OrderProcessing.ValidateOrder.Output").ComponentAddress);
        ApplicationAddress.ResolvePort("ValidateOrder.Input", "OrderProcessing")
            .ShouldBe(ApplicationAddress.WorkflowPort(
                "OrderProcessing",
                "ValidateOrder",
                "Input"));
        ApplicationAddress.ResolvePort(
                "OtherWorkflow.Source.Output",
                "OrderProcessing")
            .Value.ShouldBe("OtherWorkflow.Source.Output");
    }

    [Fact]
    public void Absolute_component_addresses_do_not_change_local_port_resolution()
    {
        ApplicationAddress.Parse("Orders.Normalize")
            .Kind.ShouldBe(ApplicationAddressKind.WorkflowComponent);
        ApplicationAddress.ResolvePort("Normalize.Input", "Orders")
            .ShouldBe(ApplicationAddress.WorkflowPort("Orders", "Normalize", "Input"));
    }

    [Fact]
    public void ReservesSystemEventAndDiagnosticOutputs()
    {
        ApplicationAddress.Parse("System.Events.Output")
            .ShouldBeSameAs(ApplicationAddress.SystemEvents);
        ApplicationAddress.ResolvePort("System.Diagnostics.Output", "Orders")
            .ShouldBeSameAs(ApplicationAddress.SystemDiagnostics);
    }

    [Fact]
    public void AddressEqualityIsOrdinalAndCaseSensitive()
    {
        ApplicationAddress.WorkflowPort("Orders", "Node", "Output")
            .ShouldNotBe(ApplicationAddress.WorkflowPort("orders", "Node", "Output"));
        ApplicationAddress.Parse("Resources.Group.Client")
            .ShouldBe(ApplicationAddress.Resource("Group", "Client"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" Orders.Node.Output")]
    [InlineData("Orders..Output")]
    [InlineData("Orders.Node.Output.Value")]
    [InlineData("Resources")]
    [InlineData("System.Events.Input")]
    [InlineData("System.events.Output")]
    public void ParseRejectsInvalidAbsoluteAddresses(string value)
        => Should.Throw<FormatException>(() => ApplicationAddress.Parse(value));

    [Theory]
    [InlineData("Resources.Client", "Orders")]
    [InlineData("System.Events", "Orders")]
    [InlineData("Node", "Orders")]
    [InlineData("Node.Port.More.Value", "Orders")]
    [InlineData("Resources.Client.Host", "Orders")]
    [InlineData("Orders.Resources.Client", "Orders")]
    [InlineData("System.Events.Input", "Orders")]
    [InlineData("System.events.Output", "Orders")]
    [InlineData("Other..Output", "Orders")]
    [InlineData("Other.Node. Output", "Orders")]
    public void ResolvePortRejectsNonPortReferences(string reference, string workflow)
        => Should.Throw<FormatException>(() =>
            ApplicationAddress.ResolvePort(reference, workflow));

    [Theory]
    [InlineData("Resources")]
    [InlineData("System")]
    public void WorkflowPortRejectsReservedWorkflowNames(string workflow)
    {
        Should.Throw<ArgumentException>(() =>
            ApplicationAddress.WorkflowPort(workflow, "Node", "Output"));
        Should.Throw<ArgumentException>(() =>
            ApplicationAddress.WorkflowComponent(workflow, "Node"));
    }

    [Theory]
    [InlineData("Orders", "Node", "Output")]
    [InlineData("orders", "node", "output")]
    [InlineData("Orders", "node-name", "output-name")]
    public void Port_construction_and_resolution_preserve_canonical_identity(string workflow, string component, string port)
    {
        var address = ApplicationAddress.WorkflowPort(workflow, component, port);
        address.ShouldBe(ApplicationAddress.WorkflowComponent(workflow, component).Port(port));
        address.ShouldBe(ApplicationAddress.Parse(address.Value));
        address.ShouldBe(ApplicationAddress.ResolvePort(address.Value, "Other"));
        address.ShouldBe(ApplicationAddress.ResolvePort($"{component}.{port}", workflow));
        address.Segments.ShouldBe([workflow, component, port]);
        address.GetHashCode().ShouldBe(ApplicationAddress.Parse(address.Value).GetHashCode());
    }

    [Theory]
    [InlineData("bad.name", "Node", "Output", "workflow")]
    [InlineData("Orders", "bad.name", "Output", "component")]
    [InlineData("Orders", "Node", "bad.name", "port")]
    [InlineData("Orders", " Node", "Output", "component")]
    [InlineData("Orders", "Node", "", "port")]
    public void Port_construction_identifies_the_invalid_segment(string workflow, string component, string port, string parameter)
        => Should.Throw<ArgumentException>(() => ApplicationAddress.WorkflowPort(workflow, component, port))
            .ParamName.ShouldBe(parameter);

    [Fact]
    public void TryMethodsReturnFalseWithoutPartialAddresses()
    {
        ApplicationAddress.TryParse("bad", out var absolute).ShouldBeFalse();
        absolute.ShouldBeNull();
        ApplicationAddress.TryResolvePort("bad", "Orders", out var resolved).ShouldBeFalse();
        resolved.ShouldBeNull();
    }
}
