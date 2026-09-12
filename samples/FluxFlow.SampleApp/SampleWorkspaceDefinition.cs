using FluxFlow.Composition;
using FluxFlow.Composition.Authoring;
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
            .AddComponent(
                "source",
                SampleComponents.OrderSource,
                options => options.Orders =
                [
                    new SampleOrder("A-100", "Harbor Market", 125m),
                    new SampleOrder("A-101", "Cedar Supply", 42m),
                    new SampleOrder("A-102", "Summit Works", 230m)
                ],
                out var source)
            .AddComponent(
                "review",
                SampleComponents.OrderReview,
                out var review)
            .AddComponent(
                "priority",
                SampleComponents.OrderSink,
                options => options.Category = "priority",
                out var priority)
            .AddComponent(
                "standard",
                SampleComponents.OrderSink,
                options => options.Category = "standard",
                out var standard)
            .AddComponent(
                "events",
                SampleComponents.EventCollector,
                out var events);

        source.Output.LinkTo(review.Input);
        review.Output
            .LinkTo(priority.Input, when: static value =>
                value.Deserialize<ReviewedOrder>()!.Priority)
            .LinkTo(standard.Input, when: static value =>
                !value.Deserialize<ReviewedOrder>()!.Priority);
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
