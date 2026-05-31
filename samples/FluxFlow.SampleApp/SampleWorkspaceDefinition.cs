using FluxFlow.Engine.Definitions;
using System.Text.Json;

namespace FluxFlow.SampleApp;

internal sealed record SampleWorkspaceDefinition
{
    public required string Name { get; init; }
    public Dictionary<string, NodeDefinition> Resources { get; init; } = [];
    public Dictionary<string, WorkflowDefinition> Workflows { get; init; } = [];
    public Dictionary<string, SampleViewDefinition> Views { get; init; } = [];
    public Dictionary<string, SampleCheckDefinition> Checks { get; init; } = [];

    public ApplicationDefinition ToEngineDefinition()
        => new()
        {
            Resources = new Dictionary<string, NodeDefinition>(Resources, StringComparer.Ordinal),
            Workflows = new Dictionary<string, WorkflowDefinition>(Workflows, StringComparer.Ordinal)
        };

    public static SampleWorkspaceDefinition CreateDefault()
        => new()
        {
            Name = "sample-order-workspace",
            Workflows =
            {
                ["main"] = new WorkflowDefinition
                {
                    Nodes =
                    {
                        ["source"] = new NodeDefinition
                        {
                            Type = SampleNodeTypes.OrderSource,
                            Configuration =
                            {
                                ["orders"] = JsonValue(new[]
                                {
                                    new SampleOrder("A-100", "Harbor Market", 125m),
                                    new SampleOrder("A-101", "Cedar Supply", 42m),
                                    new SampleOrder("A-102", "Summit Works", 230m)
                                })
                            }
                        },
                        ["review"] = new NodeDefinition
                        {
                            Type = SampleNodeTypes.OrderReview,
                            Ports =
                            {
                                ["Input"] = JsonValue("source.Output")
                            }
                        },
                        ["priority"] = new NodeDefinition
                        {
                            Type = SampleNodeTypes.OrderSink,
                            Configuration =
                            {
                                ["category"] = JsonValue("priority")
                            },
                            Ports =
                            {
                                ["Input"] = JsonValue(new
                                {
                                    from = "review.Output",
                                    when = "input.Priority == true"
                                })
                            }
                        },
                        ["standard"] = new NodeDefinition
                        {
                            Type = SampleNodeTypes.OrderSink,
                            Configuration =
                            {
                                ["category"] = JsonValue("standard")
                            },
                            Ports =
                            {
                                ["Input"] = JsonValue(new
                                {
                                    from = "review.Output",
                                    when = "input.Priority == false"
                                })
                            }
                        }
                    }
                }
            },
            Views =
            {
                ["operations"] = new SampleViewDefinition("main", "Order operations")
            },
            Checks =
            {
                ["priority-route"] = new SampleCheckDefinition("main", "priority")
            }
        };

    private static JsonElement JsonValue<T>(T value)
        => JsonSerializer.SerializeToElement(value);
}

internal sealed record SampleViewDefinition(string Workflow, string Title);

internal sealed record SampleCheckDefinition(string Workflow, string Node);
