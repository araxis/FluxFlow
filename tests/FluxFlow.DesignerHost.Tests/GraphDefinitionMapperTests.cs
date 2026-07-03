using System.Text.Json;
using FluxFlow.Composition;
using Shouldly;
using Xunit;

namespace FluxFlow.DesignerHost.Tests;

public sealed class GraphDefinitionMapperTests
{
    [Fact]
    public void Definition_to_graphs_to_definition_round_trip_is_lossless()
    {
        var original = CreateSampleDefinition();

        var roundTripped = GraphDefinitionMapper.ToDefinition(
            GraphDefinitionMapper.FromDefinition(original));

        SerializeDefinition(roundTripped).ShouldBe(SerializeDefinition(original));
    }

    [Fact]
    public void Graph_to_definition_to_graph_preserves_nodes_and_links()
    {
        var graph = new GraphModel
        {
            WorkflowName = "main",
            Nodes =
            [
                new GraphNodeModel
                {
                    Name = "source",
                    ComponentType = "timer.interval",
                    Options = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                    {
                        ["interval"] = JsonSerializer.SerializeToElement("00:00:01")
                    },
                    Resources = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["clock"] = "system-clock"
                    },
                    Layout = new GraphLayoutModel { X = 40, Y = 80 }
                },
                new GraphNodeModel { Name = "sink", ComponentType = "sample.sink" }
            ],
            Links =
            [
                new GraphLinkModel
                {
                    FromNode = "source",
                    FromPort = "Output",
                    ToNode = "sink",
                    ToPort = "Input"
                }
            ]
        };

        var restored = GraphDefinitionMapper.FromDefinition(
            GraphDefinitionMapper.ToDefinition(graph), "main");

        restored.WorkflowName.ShouldBe("main");
        restored.Nodes.Count.ShouldBe(2);
        var source = restored.Nodes.Single(node => node.Name == "source");
        source.ComponentType.ShouldBe("timer.interval");
        source.Options["interval"].GetString().ShouldBe("00:00:01");
        source.Resources["clock"].ShouldBe("system-clock");
        var link = restored.Links.ShouldHaveSingleItem();
        link.FromNode.ShouldBe("source");
        link.FromPort.ShouldBe("Output");
        link.ToNode.ShouldBe("sink");
        link.ToPort.ShouldBe("Input");
        link.FromWorkflow.ShouldBeNull();
        link.ToWorkflow.ShouldBeNull();
    }

    [Fact]
    public void Layout_is_host_only_and_never_reaches_the_definition()
    {
        var graph = new GraphModel
        {
            WorkflowName = "main",
            Nodes =
            [
                new GraphNodeModel
                {
                    Name = "source",
                    ComponentType = "sample.source",
                    Layout = new GraphLayoutModel { X = 123, Y = 456 }
                }
            ]
        };
        var movedGraph = graph with
        {
            Nodes = [graph.Nodes[0] with { Layout = new GraphLayoutModel { X = 9, Y = 9 } }]
        };

        SerializeDefinition(GraphDefinitionMapper.ToDefinition(graph))
            .ShouldBe(SerializeDefinition(GraphDefinitionMapper.ToDefinition(movedGraph)));

        var restored = GraphDefinitionMapper.FromDefinition(
            GraphDefinitionMapper.ToDefinition(graph), "main");
        restored.Nodes.Single().Layout.ShouldBe(new GraphLayoutModel());
    }

    [Fact]
    public void Cross_workflow_link_segments_survive_the_round_trip()
    {
        var original = new CompositionDefinition
        {
            Workflows = new Dictionary<string, WorkflowDefinition>(StringComparer.Ordinal)
            {
                ["main"] = new WorkflowDefinition
                {
                    Nodes = new Dictionary<string, NodeDefinition>(StringComparer.Ordinal)
                    {
                        ["sink"] = new NodeDefinition { Type = "sample.sink" }
                    },
                    Links =
                    [
                        new LinkDefinition
                        {
                            From = PortReference.Parse("side.source.Output"),
                            To = PortReference.Parse("sink.Input")
                        }
                    ]
                },
                ["side"] = new WorkflowDefinition
                {
                    Nodes = new Dictionary<string, NodeDefinition>(StringComparer.Ordinal)
                    {
                        ["source"] = new NodeDefinition { Type = "sample.source" }
                    }
                }
            }
        };

        var graphs = GraphDefinitionMapper.FromDefinition(original);
        var mainGraph = graphs.Single(graph => graph.WorkflowName == "main");
        var link = mainGraph.Links.ShouldHaveSingleItem();
        link.FromWorkflow.ShouldBe("side");
        link.ToWorkflow.ShouldBeNull();

        SerializeDefinition(GraphDefinitionMapper.ToDefinition(graphs))
            .ShouldBe(SerializeDefinition(original));
    }

    [Fact]
    public void Unknown_workflow_name_throws_a_clear_error()
    {
        var definition = GraphDefinitionMapper.ToDefinition(
            new GraphModel { WorkflowName = "main" });

        var exception = Should.Throw<InvalidOperationException>(
            () => GraphDefinitionMapper.FromDefinition(definition, "missing"));

        exception.Message.ShouldContain("missing");
    }

    [Fact]
    public void Duplicate_workflow_names_are_rejected()
    {
        var graphs = new[]
        {
            new GraphModel { WorkflowName = "main" },
            new GraphModel { WorkflowName = "main" }
        };

        Should.Throw<InvalidOperationException>(() => GraphDefinitionMapper.ToDefinition(graphs))
            .Message.ShouldContain("main");
    }

    private static CompositionDefinition CreateSampleDefinition()
        => new()
        {
            Workflows = new Dictionary<string, WorkflowDefinition>(StringComparer.Ordinal)
            {
                ["main"] = new WorkflowDefinition
                {
                    Nodes = new Dictionary<string, NodeDefinition>(StringComparer.Ordinal)
                    {
                        ["source"] = new NodeDefinition
                        {
                            Type = "timer.interval",
                            Configuration = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                            {
                                ["interval"] = JsonSerializer.SerializeToElement("00:00:01"),
                                ["count"] = JsonSerializer.SerializeToElement(5),
                                ["enabled"] = JsonSerializer.SerializeToElement(true)
                            },
                            Resources = new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["clock"] = "system-clock"
                            }
                        },
                        ["sink"] = new NodeDefinition { Type = "sample.sink" }
                    },
                    Links =
                    [
                        new LinkDefinition
                        {
                            From = PortReference.Parse("source.Output"),
                            To = PortReference.Parse("sink.Input")
                        }
                    ]
                },
                ["audit"] = new WorkflowDefinition
                {
                    Nodes = new Dictionary<string, NodeDefinition>(StringComparer.Ordinal)
                    {
                        ["log"] = new NodeDefinition { Type = "observability.logger" }
                    }
                }
            }
        };

    private static string SerializeDefinition(CompositionDefinition definition)
        => JsonSerializer.Serialize(definition, CompositionDefinitionJson.CreateSerializerOptions());
}
