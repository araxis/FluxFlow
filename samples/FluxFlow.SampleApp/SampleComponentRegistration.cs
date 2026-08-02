using FluxFlow.Composition;

namespace FluxFlow.SampleApp;

internal static class SampleComponentTypes
{
    public const string OrderSource = "sample.order-source";
    public const string OrderReview = "sample.order-review";
    public const string OrderSink = "sample.order-sink";
    public const string EventCollector = "sample.event-collector";
}

internal static class SampleComponentRegistration
{
    public static FluxFlowRegistrationBuilder AddSampleOrderComponents(
        this FluxFlowRegistrationBuilder builder,
        InMemoryOrderStore store,
        InMemoryComponentEventCollector events)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(events);

        return builder
            .AddRuntimeComponent(SampleComponentTypes.OrderSource, component =>
            {
                component.UseFactory(OrderSourceNode.Create);
                component.AddOutput<SampleOrder>("Output");
            })
            .AddRuntimeComponent(SampleComponentTypes.OrderReview, component =>
            {
                component.UseFactory(OrderReviewNode.Create);
                component.AddInput<SampleOrder>("Input");
                component.AddOutput<ReviewedOrder>("Output");
            })
            .AddRuntimeComponent(SampleComponentTypes.OrderSink, component =>
            {
                component.UseFactory(context => OrderSinkNode.Create(context, store));
                component.AddInput<ReviewedOrder>("Input");
            })
            .AddRuntimeComponent(SampleComponentTypes.EventCollector, component =>
            {
                component.UseFactory(context => EventCollectorNode.Create(context, events));
                component.AddInput<ComponentEvent>("Input");
            });
    }
}
