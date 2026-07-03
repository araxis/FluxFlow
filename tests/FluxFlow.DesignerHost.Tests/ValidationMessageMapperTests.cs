using FluxFlow.Components.Designer;
using FluxFlow.Composition;
using Shouldly;
using Xunit;

namespace FluxFlow.DesignerHost.Tests;

public sealed class ValidationMessageMapperTests
{
    [Fact]
    public void Metadata_errors_map_with_path_prefix_and_component_type()
    {
        var message = ValidationMessageMapper.FromMetadataError(
            new DesignerMetadataValidationError("Options[0].Name", "Option names are required."),
            componentType: "sample.widget");

        message.Severity.ShouldBe(ValidationSeverity.Error);
        message.Source.ShouldBe(ValidationSource.Metadata);
        message.Message.ShouldBe("Options[0].Name: Option names are required.");
        message.ComponentType.ShouldBe("sample.widget");
        message.NodeName.ShouldBeNull();
    }

    [Fact]
    public void Composition_diagnostics_map_with_node_context()
    {
        var message = ValidationMessageMapper.FromDiagnostic(new CompositionDiagnostic
        {
            Code = CompositionDiagnosticCode.UnknownNodeType,
            Message = "Node type 'sample.missing' is not registered.",
            WorkflowName = "main",
            NodeName = "source"
        });

        message.Severity.ShouldBe(ValidationSeverity.Error);
        message.Source.ShouldBe(ValidationSource.Composition);
        message.Message.ShouldBe("Node type 'sample.missing' is not registered.");
        message.NodeName.ShouldBe("source");
    }

    [Fact]
    public void Diagnostic_lists_map_in_order()
    {
        var messages = ValidationMessageMapper.FromDiagnostics(
        [
            new CompositionDiagnostic
            {
                Code = CompositionDiagnosticCode.UnknownNodeType,
                Message = "first"
            },
            new CompositionDiagnostic
            {
                Code = CompositionDiagnosticCode.FactoryFailed,
                Message = "second"
            }
        ]);

        messages.Select(message => message.Message).ShouldBe(["first", "second"]);
    }
}
