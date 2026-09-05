using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using FluxFlow.Data;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Designer.Tests;

public sealed class InputShapeMetadataTests
{
    [Theory]
    [InlineData(PortDirection.Output, ComponentPortKind.Message, false)]
    [InlineData(PortDirection.Input, ComponentPortKind.Signal, false)]
    [InlineData(PortDirection.Input, ComponentPortKind.Message, true)]
    public void Shape_on_noncanonical_input_reports_the_metadata_path(
        PortDirection direction, ComponentPortKind kind, bool typed)
    {
        var metadata = new ComponentDesignMetadata
        {
            Type = new ComponentType("sample.request"),
            Ports = [new PortDesignMetadata
            {
                Name = new ComponentPortName("Input"),
                Direction = direction,
                Kind = kind,
                MessageType = typed ? typeof(string) : null,
                InputShape = FlowValueShape.Object("Request")
            }]
        };

        var error = ComponentDesignMetadataValidator.Validate(metadata).ShouldHaveSingleItem();

        error.Path.ShouldBe("Ports[0].InputShape");
        error.Message.ShouldContain("canonical value input");
    }
}
