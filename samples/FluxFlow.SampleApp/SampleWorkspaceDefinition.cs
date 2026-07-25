using System.Text.Json;
using FluxFlow.Composition.Model;
using ApplicationWorkflowDefinition = FluxFlow.Composition.Model.WorkflowDefinition;

namespace FluxFlow.SampleApp;

internal sealed record SampleWorkspaceDefinition
{
    public required string Name { get; init; }
    public Dictionary<string, ResourceDefinition> Resources { get; init; } = [];
    public Dictionary<string, ApplicationWorkflowDefinition> Workflows { get; init; } = [];
    public Dictionary<string, SampleViewDefinition> Views { get; init; } = [];
    public Dictionary<string, SampleCheckDefinition> Checks { get; init; } = [];

    public ApplicationDefinition ToApplicationDefinition()
        => new(Resources, Workflows);

    public static SampleWorkspaceDefinition CreateDefault()
        => new()
        {
            Name = "sample-order-workspace",
            Workflows =
            {
                ["main"] = new ApplicationWorkflowDefinition(
                [
                    new("source", Component(
                        SampleComponentTypes.OrderSource,
                        ("orders", new[]
                        {
                            new SampleOrder("A-100", "Harbor Market", 125m),
                            new SampleOrder("A-101", "Cedar Supply", 42m),
                            new SampleOrder("A-102", "Summit Works", 230m)
                        }))),
                    new("review", Component(
                        SampleComponentTypes.OrderReview,
                        ("Input", "source.Output"))),
                    new("priority", Component(
                        SampleComponentTypes.OrderSink,
                        ("category", "priority"),
                        ("Input", new
                        {
                            Port = "review.Output",
                            Condition = "input.Priority == true"
                        }))),
                    new("standard", Component(
                        SampleComponentTypes.OrderSink,
                        ("category", "standard"),
                        ("Input", new
                        {
                            Port = "review.Output",
                            Condition = "input.Priority == false"
                        }))),
                    new("events", Component(
                        SampleComponentTypes.EventCollector,
                        ("Input", new[]
                        {
                            "review.Events",
                            "priority.Events",
                            "standard.Events"
                        })))
                ])
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

    private static ComponentDefinition Component(
        string type,
        params (string Name, object? Value)[] properties)
        => new(
            type,
            properties.Select(property => KeyValuePair.Create(
                property.Name,
                JsonSerializer.SerializeToElement(property.Value))));
}

internal sealed record SampleViewDefinition(string Workflow, string Title);

internal sealed record SampleCheckDefinition(string Workflow, string Component);
