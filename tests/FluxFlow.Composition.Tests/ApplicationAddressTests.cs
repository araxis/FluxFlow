using FluxFlow.Composition.Addressing;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class ApplicationAddressTests
{
    [Fact]
    public void ParsesNestedResourcesAndAbsoluteWorkflowPorts()
    {
        var resource = ApplicationAddress.Parse("Resources.Messaging.Client1");
        var port = ApplicationAddress.Parse("OrderProcessing.ValidateOrder.Input");

        resource.Kind.ShouldBe(ApplicationAddressKind.Resource);
        resource.Segments.ShouldBe(["Resources", "Messaging", "Client1"]);
        resource.Value.ShouldBe("Resources.Messaging.Client1");
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
    [InlineData("Orders.Node")]
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
        => Should.Throw<ArgumentException>(() =>
            ApplicationAddress.WorkflowPort(workflow, "Node", "Output"));

    [Fact]
    public void TryMethodsReturnFalseWithoutPartialAddresses()
    {
        ApplicationAddress.TryParse("bad", out var absolute).ShouldBeFalse();
        absolute.ShouldBeNull();
        ApplicationAddress.TryResolvePort("bad", "Orders", out var resolved).ShouldBeFalse();
        resolved.ShouldBeNull();
    }
}
