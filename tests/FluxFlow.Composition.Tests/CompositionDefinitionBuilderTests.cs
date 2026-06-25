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

    [Fact]
    public void Definition_collections_are_copied_on_assignment()
    {
        var sourceNode = new NodeDefinition
        {
            Type = TestNodeTypes.Source
        };
        var workflow = new WorkflowDefinition
        {
            Nodes = new Dictionary<string, NodeDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["source"] = sourceNode
            }
        };
        var workflows = new Dictionary<string, WorkflowDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["main"] = workflow
        };

        var definition = new CompositionDefinition
        {
            Workflows = workflows
        };

        workflows["other"] = new WorkflowDefinition();

        definition.Workflows.ContainsKey("main").ShouldBeTrue();
        definition.Workflows.ContainsKey("MAIN").ShouldBeFalse();
        definition.Workflows.ContainsKey("other").ShouldBeFalse();
    }

    [Fact]
    public void Workflow_collections_are_copied_on_assignment()
    {
        var nodes = new Dictionary<string, NodeDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["source"] = new NodeDefinition { Type = TestNodeTypes.Source }
        };
        var links = new List<LinkDefinition>
        {
            new()
            {
                From = new PortReference { Node = "source", Port = "Output" },
                To = new PortReference { Node = "sink", Port = "Input" }
            }
        };

        var workflow = new WorkflowDefinition
        {
            Nodes = nodes,
            Links = links
        };

        nodes["sink"] = new NodeDefinition { Type = TestNodeTypes.Sink };
        links.Add(new LinkDefinition
        {
            From = new PortReference { Node = "source", Port = "Other" },
            To = new PortReference { Node = "sink", Port = "Other" }
        });

        workflow.Nodes.ContainsKey("source").ShouldBeTrue();
        workflow.Nodes.ContainsKey("SOURCE").ShouldBeFalse();
        workflow.Nodes.ContainsKey("sink").ShouldBeFalse();
        workflow.Links.Count.ShouldBe(1);
    }

    [Fact]
    public void Node_collections_are_copied_on_assignment()
    {
        var configuration = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["messages"] = JsonSerializer.SerializeToElement(new[] { "one" })
        };
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["store"] = "primary"
        };

        var node = new NodeDefinition
        {
            Type = TestNodeTypes.Source,
            Configuration = configuration,
            Resources = resources
        };

        configuration["extra"] = JsonSerializer.SerializeToElement(true);
        resources["clock"] = "test-clock";

        node.Configuration.ContainsKey("messages").ShouldBeTrue();
        node.Configuration.ContainsKey("MESSAGES").ShouldBeFalse();
        node.Configuration.ContainsKey("extra").ShouldBeFalse();
        node.Resources.ContainsKey("store").ShouldBeTrue();
        node.Resources.ContainsKey("STORE").ShouldBeFalse();
        node.Resources.ContainsKey("clock").ShouldBeFalse();
    }
}
