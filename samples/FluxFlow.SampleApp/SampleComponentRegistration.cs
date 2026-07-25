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
    public static CompositionNodeRegistry RegisterSampleOrderComponents(
        this CompositionNodeRegistry registry,
        InMemoryOrderStore store,
        InMemoryComponentEventCollector events)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(events);

        return registry
            .Register(
                SampleComponentTypes.OrderSource,
                OrderSourceNode.Create,
                outputs: [CompositionPorts.Metadata<SampleOrder>("Output")])
            .Register(
                SampleComponentTypes.OrderReview,
                OrderReviewNode.Create,
                inputs: [CompositionPorts.Metadata<SampleOrder>("Input")],
                outputs: [CompositionPorts.Metadata<ReviewedOrder>("Output")])
            .Register(
                SampleComponentTypes.OrderSink,
                context => OrderSinkNode.Create(context, store),
                inputs: [CompositionPorts.Metadata<ReviewedOrder>("Input")])
            .Register(
                SampleComponentTypes.EventCollector,
                context => EventCollectorNode.Create(context, events),
                inputs: [CompositionPorts.Metadata<CompositionComponentEvent>("Input")]);
    }
}
