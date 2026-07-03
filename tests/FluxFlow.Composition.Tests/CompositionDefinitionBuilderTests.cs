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
    public void Fluent_builder_trims_workflow_node_configuration_and_resource_names()
    {
        var definition = CompositionDefinitionBuilder
            .Create()
            .Workflow(" main ", workflow => workflow
                .Node(" source ", TestNodeTypes.Source, node => node
                    .Configure(" messages ", new[] { "one" })
                    .Resource(" store ", "primary-store")))
            .Build();

        definition.Workflows.Keys.ShouldBe(["main"]);
        var workflow = definition.Workflows["main"];
        workflow.Nodes.Keys.ShouldBe(["source"]);
        workflow.Nodes["source"].Configuration.Keys.ShouldBe(["messages"]);
        workflow.Nodes["source"].Resources.Keys.ShouldBe(["store"]);
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
    public void Fluent_builder_rejects_duplicate_names_after_trimming()
    {
        Should.Throw<InvalidOperationException>(() =>
            CompositionDefinitionBuilder
                .Create()
                .Workflow("main", _ => { })
                .Workflow(" main ", _ => { }))
            .Message.ShouldContain("already defined");

        Should.Throw<InvalidOperationException>(() =>
            CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow
                    .Node("source", TestNodeTypes.Source)
                    .Node(" source ", TestNodeTypes.Source)))
            .Message.ShouldContain("already defined");
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
    public void Definition_collections_trim_keys_on_assignment()
    {
        var definition = new CompositionDefinition
        {
            Workflows = new Dictionary<string, WorkflowDefinition>(StringComparer.Ordinal)
            {
                [" main "] = new()
            }
        };

        definition.Workflows.Keys.ShouldBe(["main"]);
    }

    [Fact]
    public void Definition_collections_reject_duplicate_keys_after_trimming()
    {
        var exception = Should.Throw<ArgumentException>(() =>
            new CompositionDefinition
            {
                Workflows = new Dictionary<string, WorkflowDefinition>(StringComparer.Ordinal)
                {
                    ["main"] = new(),
                    [" main "] = new()
                }
            });

        exception.Message.ShouldContain("duplicate key 'main'");
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
    public void Workflow_collections_trim_node_keys_and_reject_duplicate_normalized_keys()
    {
        var workflow = new WorkflowDefinition
        {
            Nodes = new Dictionary<string, NodeDefinition>(StringComparer.Ordinal)
            {
                [" source "] = new() { Type = TestNodeTypes.Source }
            }
        };

        workflow.Nodes.Keys.ShouldBe(["source"]);

        var exception = Should.Throw<ArgumentException>(() =>
            new WorkflowDefinition
            {
                Nodes = new Dictionary<string, NodeDefinition>(StringComparer.Ordinal)
                {
                    ["source"] = new() { Type = TestNodeTypes.Source },
                    [" source "] = new() { Type = TestNodeTypes.Sink }
                }
            });
        exception.Message.ShouldContain("duplicate key 'source'");
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

    [Fact]
    public void Node_collections_trim_configuration_and_resource_keys()
    {
        var node = new NodeDefinition
        {
            Type = TestNodeTypes.Source,
            Configuration = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                [" messages "] = JsonSerializer.SerializeToElement(new[] { "one" })
            },
            Resources = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [" store "] = "primary"
            }
        };

        node.Configuration.Keys.ShouldBe(["messages"]);
        node.Resources.Keys.ShouldBe(["store"]);
    }

    [Fact]
    public void Node_collections_reject_duplicate_keys_after_trimming()
    {
        Should.Throw<ArgumentException>(() =>
            new NodeDefinition
            {
                Type = TestNodeTypes.Source,
                Configuration = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["messages"] = JsonSerializer.SerializeToElement(new[] { "one" }),
                    [" messages "] = JsonSerializer.SerializeToElement(new[] { "two" })
                }
            })
            .Message.ShouldContain("duplicate key 'messages'");

        Should.Throw<ArgumentException>(() =>
            new NodeDefinition
            {
                Type = TestNodeTypes.Source,
                Resources = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["store"] = "primary",
                    [" store "] = "secondary"
                }
            })
            .Message.ShouldContain("duplicate key 'store'");
    }
}
