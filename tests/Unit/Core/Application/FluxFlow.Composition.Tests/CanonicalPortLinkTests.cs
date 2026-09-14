using FluxFlow.Testing;
using System.Text.Json;
using FluxFlow.Composition.Definition;
using FluxFlow.Composition.Links;
using FluxFlow.Json.Model;
using FluxFlow.Data;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class CanonicalPortLinkTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Link_compilation_accepts_canonical_ports_without_payload_schemas(bool codeAuthored)
    {
        var catalog = LinkCompilerFixture.Surfaces(
        [
            LinkCompilerFixture.Surface("test.source", outputs: [ComponentPortMetadata.Create("Output")]),
            LinkCompilerFixture.Surface("test.destination", inputs: [ComponentPortMetadata.Create("Input")])
        ]);
        ApplicationSetup definition;
        if (codeAuthored)
        {
            var application = new ApplicationDefinitionBuilder();
            var workflow = application.AddWorkflow("Main");
            var source = workflow.AddComponent("Source", "test.source");
            var destination = workflow.AddComponent("Destination", "test.destination");
            source.Output<FlowValue>("Output").LinkTo(destination.Input<FlowValue>("Input"));
            definition = application.Build();
        }
        else
        {
            definition = new ApplicationSetup(ApplicationDefinitionJson.Deserialize(
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
                """));
        }

        var result = new LinkCompilerFixture(catalog).Compile(definition);

        result.IsValid.ShouldBeTrue();
        result.Diagnostics.ShouldBeEmpty();
        var link = result.Links.ShouldHaveSingleItem();
        link.Source.Value.ShouldBe("Main.Source.Output");
        link.Target.Value.ShouldBe("Main.Destination.Input");
        link.MessageType.ShouldBe(typeof(FlowValue));
    }

}
