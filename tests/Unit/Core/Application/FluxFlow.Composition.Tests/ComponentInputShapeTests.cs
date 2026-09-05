using System.Text.Json;
using FluxFlow.Composition.Authoring;
using FluxFlow.Composition.Links;
using FluxFlow.Composition.Model;
using FluxFlow.Data;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class ComponentInputShapeTests
{
    [Theory]
    [InlineData("{}", false, "target")]
    [InlineData("42", false, "Number")]
    [InlineData("\"orders\"", false, "String")]
    [InlineData("null", false, "Null")]
    [InlineData("{\"TARGET\":\"orders\"}", true, null)]
    [InlineData("{\"target\":null}", true, null)]
    public void Sample_preflight_checks_structure_without_materializing(string json, bool valid, string? errorPart)
    {
        var descriptor = new ComponentDescriptor("test.request", UnusedFactory,
            inputs: [ComponentPortMetadata.CreateFlowValueInput("Input", FlowValueShape.Object("Request", "target"))]);
        var sample = FlowValue.From(JsonSerializer.Deserialize<JsonElement>(json));

        var result = descriptor.ValidateInputShape("Input", sample).ShouldNotBeNull();

        result.IsValid.ShouldBe(valid);
        if (errorPart is null)
            result.Error.ShouldBeNull();
        else
            result.Error.ShouldNotBeNull().ShouldContain(errorPart);
    }

    [Fact]
    public void Undescribed_input_is_not_checked_and_unknown_input_is_reported()
    {
        var descriptor = new ComponentDescriptor("test.dynamic", UnusedFactory,
            inputs: [ComponentPortMetadata.Create<FlowValue>("Input")]);

        descriptor.ValidateInputShape("Input", FlowValue.Null).ShouldBeNull();
        var exception = Should.Throw<ArgumentException>(() =>
            descriptor.ValidateInputShape("Missing", FlowValue.Null));
        exception.ParamName.ShouldBe("inputName");
        exception.Message.ShouldContain("test.dynamic");
        exception.Message.ShouldContain("Missing");
    }

    [Fact]
    public void Descriptor_rejects_input_shape_on_output_port()
    {
        var exception = Should.Throw<ArgumentException>(() => new ComponentDescriptor(
            "test.invalid-output", UnusedFactory,
            outputs: [ComponentPortMetadata.CreateFlowValueInput("Output", FlowValueShape.String("Text"))]));

        exception.ParamName.ShouldBe("outputs");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Link_compilation_accepts_unknown_canonical_output_with_restrictive_destination_shape(bool codeAuthored)
    {
        var catalog = new ComponentCatalog(
        [
            new ComponentDescriptor("test.source", UnusedFactory,
                outputs: [ComponentPortMetadata.Create<FlowValue>("Output")]),
            new ComponentDescriptor("test.destination", UnusedFactory,
                inputs: [ComponentPortMetadata.CreateFlowValueInput("Input", FlowValueShape.Object("Request", "target"))])
        ]);
        ApplicationDefinition definition;
        if (codeAuthored)
        {
            var application = new ApplicationDefinitionBuilder();
            var workflow = application.AddWorkflow("Main");
            var source = workflow.AddComponent("Source", "test.source");
            var destination = workflow.AddComponent("Destination", "test.destination");
            source.Output<FlowValue>("Output").ConnectTo(destination.Input<FlowValue>("Input"));
            definition = application.Build();
        }
        else
        {
            definition = ApplicationDefinitionJson.Deserialize(
                """
                {
                  "Resources": {},
                  "Workflows": {
                    "Main": {
                      "Source": { "Type": "test.source", "Output": "Destination.Input" },
                      "Destination": { "Type": "test.destination" }
                    }
                  }
                }
                """);
        }

        var result = new ApplicationLinkCompiler(catalog).Compile(definition);

        result.IsValid.ShouldBeTrue();
        result.Diagnostics.ShouldBeEmpty();
        var link = result.Links.ShouldHaveSingleItem();
        link.Source.Value.ShouldBe("Main.Source.Output");
        link.Target.Value.ShouldBe("Main.Destination.Input");
        link.MessageType.ShouldBe(typeof(FlowValue));
    }

    private static ValueTask<ComponentInstance> UnusedFactory(ComponentActivationContext _)
        => throw new InvalidOperationException("Metadata inspection must not activate a component.");
}
