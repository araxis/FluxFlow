using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class CompositionReferenceTests
{
    [Theory]
    [InlineData("source", null, "source")]
    [InlineData(" main.source ", "main", "source")]
    public void Node_reference_parse_trims_segments(
        string value,
        string? expectedWorkflow,
        string expectedNode)
    {
        var reference = NodeReference.Parse(value);

        reference.Workflow.ShouldBe(expectedWorkflow);
        reference.Node.ShouldBe(expectedNode);
    }

    [Theory]
    [InlineData("source.Output", null, "source", "Output")]
    [InlineData(" main.source.Output ", "main", "source", "Output")]
    public void Port_reference_parse_trims_segments(
        string value,
        string? expectedWorkflow,
        string expectedNode,
        string expectedPort)
    {
        var reference = PortReference.Parse(value);

        reference.Workflow.ShouldBe(expectedWorkflow);
        reference.Node.ShouldBe(expectedNode);
        reference.Port.ShouldBe(expectedPort);
    }

    [Theory]
    [InlineData(".source")]
    [InlineData("main.")]
    [InlineData("main..source")]
    [InlineData("main.source.extra")]
    public void Node_reference_parse_rejects_malformed_values(string value)
    {
        Should.Throw<FormatException>(() => NodeReference.Parse(value));
    }

    [Theory]
    [InlineData(".Output")]
    [InlineData("source.")]
    [InlineData("source..Output")]
    [InlineData("main.source.")]
    [InlineData("main..source.Output")]
    [InlineData("main.source.Output.extra")]
    public void Port_reference_parse_rejects_malformed_values(string value)
    {
        Should.Throw<FormatException>(() => PortReference.Parse(value));
    }

    [Fact]
    public void Node_reference_properties_normalize_assigned_segments()
    {
        var reference = new NodeReference
        {
            Workflow = " ",
            Node = " source "
        };
        var workflowReference = new NodeReference
        {
            Workflow = " main ",
            Node = " source "
        };

        reference.Workflow.ShouldBeNull();
        reference.Node.ShouldBe("source");
        reference.ToString().ShouldBe("source");
        workflowReference.Workflow.ShouldBe("main");
        workflowReference.Node.ShouldBe("source");
        workflowReference.ToString().ShouldBe("main.source");
    }

    [Fact]
    public void Port_reference_properties_normalize_assigned_segments()
    {
        var reference = new PortReference
        {
            Workflow = " ",
            Node = " source ",
            Port = " Output "
        };
        var workflowReference = new PortReference
        {
            Workflow = " main ",
            Node = " source ",
            Port = " Output "
        };

        reference.Workflow.ShouldBeNull();
        reference.Node.ShouldBe("source");
        reference.Port.ShouldBe("Output");
        reference.ToString().ShouldBe("source.Output");
        workflowReference.Workflow.ShouldBe("main");
        workflowReference.Node.ShouldBe("source");
        workflowReference.Port.ShouldBe("Output");
        workflowReference.ToString().ShouldBe("main.source.Output");
    }
}
