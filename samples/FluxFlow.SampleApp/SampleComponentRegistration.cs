using FluxFlow.Composition;
using Microsoft.Extensions.DependencyInjection;

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
    public static IServiceCollection AddSampleOrderComponents(
        this IServiceCollection services,
        InMemoryOrderStore store,
        InMemoryComponentEventCollector events)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(events);

        return services
            .AddFluxFlowComponent(new ComponentDescriptor(
                SampleComponentTypes.OrderSource,
                OrderSourceNode.Create,
                outputs: [ComponentPorts.Metadata<SampleOrder>("Output")]))
            .AddFluxFlowComponent(new ComponentDescriptor(
                SampleComponentTypes.OrderReview,
                OrderReviewNode.Create,
                inputs: [ComponentPorts.Metadata<SampleOrder>("Input")],
                outputs: [ComponentPorts.Metadata<ReviewedOrder>("Output")]))
            .AddFluxFlowComponent(new ComponentDescriptor(
                SampleComponentTypes.OrderSink,
                context => OrderSinkNode.Create(context, store),
                inputs: [ComponentPorts.Metadata<ReviewedOrder>("Input")]))
            .AddFluxFlowComponent(new ComponentDescriptor(
                SampleComponentTypes.EventCollector,
                context => EventCollectorNode.Create(context, events),
                inputs: [ComponentPorts.Metadata<ComponentEvent>("Input")]));
    }
}
