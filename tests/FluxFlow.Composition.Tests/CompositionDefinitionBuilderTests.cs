using System.Text.Json;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class CompositionDefinitionBuilderTests
{
    [Fact]
    public void Fluent_builder_produces_composition_definition()
    {
        var definition = CompositionDefinitionBuilder
            .Create()
            .Workflow("main", workflow => workflow
                .Node("source", TestNodeTypes.Source, node => node
                    .Configure("messages", new[] { "one", "two" })
                    .Resource("store", "primary-store"))
                .Node("sink", TestNodeTypes.Sink)
                .Link("source.Output", "sink.Input"))
            .Build();

        definition.Workflows.ContainsKey("main").ShouldBeTrue();
        var workflow = definition.Workflows["main"];
        workflow.Nodes.Count.ShouldBe(2);
        workflow.Nodes["source"].Type.ShouldBe(TestNodeTypes.Source);
        workflow.Nodes["source"].Resources["store"].ShouldBe("primary-store");
        workflow.Nodes["source"]
            .Configuration["messages"]
            .Deserialize<string[]>()
            .ShouldBe(["one", "two"]);
        workflow.Links.ShouldHaveSingleItem();
        workflow.Links[0].From.ShouldBe(new PortReference { Node = "source", Port = "Output" });
        workflow.Links[0].To.ShouldBe(new PortReference { Node = "sink", Port = "Input" });
    }

    [Fact]
    public void Fluent_builder_rejects_duplicate_node_names()
    {
        var exception = Should.Throw<InvalidOperationException>(() =>
            CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow
                    .Node("source", TestNodeTypes.Source)
                    .Node("source", TestNodeTypes.Source)));

        exception.Message.ShouldContain("already defined");
    }
}
