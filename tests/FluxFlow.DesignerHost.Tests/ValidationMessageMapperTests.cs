using FluxFlow.Components.Designer;
using FluxFlow.Composition.Links;
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
        message.ComponentName.ShouldBeNull();
    }

    [Fact]
    public void Link_diagnostics_map_with_component_context()
    {
        var message = ValidationMessageMapper.FromLinkDiagnostic(new ApplicationLinkDiagnostic
        {
            Code = ApplicationLinkDiagnosticCode.MissingInputPort,
            Message = "Input port 'Missing' does not exist.",
            WorkflowName = "main",
            ComponentName = "sink",
            PropertyName = "Input"
        });

        message.Severity.ShouldBe(ValidationSeverity.Error);
        message.Source.ShouldBe(ValidationSource.Composition);
        message.Message.ShouldBe("Input port 'Missing' does not exist.");
        message.ComponentName.ShouldBe("sink");
    }
}
