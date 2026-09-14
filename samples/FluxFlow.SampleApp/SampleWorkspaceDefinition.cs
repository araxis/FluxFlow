using System.Text.Json;
using FluxFlow.Composition;
using FluxFlow.Composition.Definition;
using FluxFlow.Json.Model;

namespace FluxFlow.SampleApp;

internal sealed record SampleWorkspaceDefinition
{
    public required string Name { get; init; }
    public required ApplicationSetup Application { get; init; }
    public Dictionary<string, SampleViewDefinition> Views { get; init; } = [];
    public Dictionary<string, SampleCheckDefinition> Checks { get; init; } = [];

    public ApplicationSetup ToApplicationDefinition() => Application;

    public static SampleWorkspaceDefinition CreateDefault()
    {
        var application = new ApplicationDefinitionBuilder()
            .AddWorkflow("main", out var main);

        main
            .AddOrderSource("source",
                options => options.Orders =
                [
                    new SampleOrder("A-100", "Harbor Market", 125m),
                    new SampleOrder("A-101", "Cedar Supply", 42m),
                    new SampleOrder("A-102", "Summit Works", 230m)
                ],
                out var source)
            .AddOrderReview("review",
                out var review)
            .AddOrderSink("priority",
                options => options.Category = "priority",
                out var priority)
            .AddOrderSink("standard",
                options => options.Category = "standard",
                out var standard)
            .AddEventCollector("events",
                out var events);

        source.Output.LinkTo(review.Input);
        review.Output
            .LinkTo(priority.Input, when: static value =>
                value.Json.Deserialize<ReviewedOrder>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Priority)
            .LinkTo(standard.Input, when: static value =>
                !value.Json.Deserialize<ReviewedOrder>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Priority);
        review.Events.LinkTo(events.Input);
        priority.Events.LinkTo(events.Input);
        standard.Events.LinkTo(events.Input);
        source.Events.DiscardUnmatched();
        events.Events.DiscardUnmatched();

        return new SampleWorkspaceDefinition
        {
            Name = "sample-order-workspace",
            Application = application.Build(),
            Views =
            {
                ["operations"] = new SampleViewDefinition("main", "Order operations")
            },
            Checks =
            {
                ["priority-route"] = new SampleCheckDefinition("main", "priority")
            }
        };
    }
}

internal sealed record SampleViewDefinition(string Workflow, string Title);

internal sealed record SampleCheckDefinition(string Workflow, string Component);
