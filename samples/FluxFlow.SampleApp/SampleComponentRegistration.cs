using FluxFlow.Composition;
using FluxFlow.Composition.Authoring;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.SampleApp;

internal static class SampleComponentTypes
{
    public const string OrderSource = "sample.order-source";
    public const string OrderReview = "sample.order-review";
    public const string OrderSink = "sample.order-sink";
    public const string EventCollector = "sample.event-collector";
}

internal static class SampleComponentPorts
{
    public const string Input = "Input";
    public const string Output = "Output";
    public const string Events = "Events";
}

internal static class SampleComponentOptions
{
    public const string Orders = "orders";
    public const string Category = "category";
}

internal static class SampleComponents
{
    public static ComponentContract<OrderSourceComponentBuilder, OrderSourceHandle> OrderSource { get; } =
        ComponentContract.Create(
            SampleComponentTypes.OrderSource,
            static runtime =>
            {
                runtime
                    .UseFactory(OrderSourceNode.Create)
                    .HasOutput(SampleComponentPorts.Output, static node => node.Output)
                    .HasEvents(SampleComponentPorts.Events, static node => node.Events);
            },
            static () => new OrderSourceComponentBuilder(),
            static (options, definition) => options.Apply(definition),
            static component => new OrderSourceHandle(component));

    public static ComponentContract<OrderReviewHandle> OrderReview { get; } =
        ComponentContract.Create(
            SampleComponentTypes.OrderReview,
            static runtime =>
            {
                runtime
                    .UseFactory(OrderReviewNode.Create)
                    .HasInput(SampleComponentPorts.Input, static node => node.Input)
                    .HasOutput(SampleComponentPorts.Output, static node => node.Output)
                    .HasEvents(SampleComponentPorts.Events, static node => node.Events);
            },
            static component => new OrderReviewHandle(component));

    public static ComponentContract<OrderSinkComponentBuilder, OrderSinkHandle> OrderSink { get; } =
        ComponentContract.Create(
            SampleComponentTypes.OrderSink,
            static runtime =>
            {
                runtime
                    .UseFactory(static context => OrderSinkNode.Create(
                        context,
                        context.Services.GetRequiredService<InMemoryOrderStore>()))
                    .HasInput(SampleComponentPorts.Input, static node => node.Input)
                    .HasEvents(SampleComponentPorts.Events, static node => node.Events);
            },
            static () => new OrderSinkComponentBuilder(),
            static (options, definition) => options.Apply(definition),
            static component => new OrderSinkHandle(component));

    public static ComponentContract<EventCollectorHandle> EventCollector { get; } =
        ComponentContract.Create(
            SampleComponentTypes.EventCollector,
            static runtime =>
            {
                runtime
                    .UseFactory(static context => EventCollectorNode.Create(
                        context,
                        context.Services.GetRequiredService<InMemoryComponentEventCollector>()))
                    .HasInput(SampleComponentPorts.Input, static node => node.Input)
                    .HasEvents(SampleComponentPorts.Events, static node => node.Events);
            },
            static component => new EventCollectorHandle(component));
}

internal sealed class OrderSourceComponentBuilder
{
    public SampleOrder[] Orders { get; set; } = [];

    internal void Apply(ComponentDefinitionBuilder definition)
        => definition.Set(SampleComponentOptions.Orders, Orders);
}

internal sealed class OrderSinkComponentBuilder
{
    public string Category { get; set; } = "default";

    internal void Apply(ComponentDefinitionBuilder definition)
        => definition.Set(SampleComponentOptions.Category, Category);
}

internal sealed class OrderSourceHandle(ComponentHandle definition) : AuthoredComponentHandle(definition)
{
    public OutputPortHandle<SampleOrder> Output { get; } = definition.Output<SampleOrder>(SampleComponentPorts.Output);
    public OutputPortHandle<ComponentEvent> Events { get; } = definition.Output<ComponentEvent>(SampleComponentPorts.Events);
}

internal sealed class OrderReviewHandle(ComponentHandle definition) : AuthoredComponentHandle(definition)
{
    public InputPortHandle<SampleOrder> Input { get; } = definition.Input<SampleOrder>(SampleComponentPorts.Input);
    public OutputPortHandle<ReviewedOrder> Output { get; } = definition.Output<ReviewedOrder>(SampleComponentPorts.Output);
    public OutputPortHandle<ComponentEvent> Events { get; } = definition.Output<ComponentEvent>(SampleComponentPorts.Events);
}

internal sealed class OrderSinkHandle(ComponentHandle definition) : AuthoredComponentHandle(definition)
{
    public InputPortHandle<ReviewedOrder> Input { get; } = definition.Input<ReviewedOrder>(SampleComponentPorts.Input);
    public OutputPortHandle<ComponentEvent> Events { get; } = definition.Output<ComponentEvent>(SampleComponentPorts.Events);
}

internal sealed class EventCollectorHandle(ComponentHandle definition) : AuthoredComponentHandle(definition)
{
    public InputPortHandle<ComponentEvent> Input { get; } = definition.Input<ComponentEvent>(SampleComponentPorts.Input);
    public OutputPortHandle<ComponentEvent> Events { get; } = definition.Output<ComponentEvent>(SampleComponentPorts.Events);
}
