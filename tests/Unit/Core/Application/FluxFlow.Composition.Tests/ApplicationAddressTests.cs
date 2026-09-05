using FluxFlow.Composition.Addressing;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class ApplicationAddressTests
{
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

    [Fact]
    public void TryMethodsReturnFalseWithoutPartialAddresses()
    {
        ApplicationAddress.TryParse("bad", out var absolute).ShouldBeFalse();
        absolute.ShouldBeNull();
        ApplicationAddress.TryResolvePort("bad", "Orders", out var resolved).ShouldBeFalse();
        resolved.ShouldBeNull();
    }
}
