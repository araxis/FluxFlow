using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.SampleApp;

internal static class SampleNodeTypes
{
    public static readonly NodeType OrderSource = new("sample.order-source");
    public static readonly NodeType OrderReview = new("sample.order-review");
    public static readonly NodeType OrderSink = new("sample.order-sink");
}

internal static class SampleComponentRegistration
{
    public static RuntimeNodeFactoryRegistry RegisterSampleOrderComponents(
        this RuntimeNodeFactoryRegistry registry,
        InMemoryOrderStore store)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(store);

        return registry
            .Register(SampleNodeTypes.OrderSource, OrderSourceNode.Create)
            .Register(SampleNodeTypes.OrderReview, OrderReviewNode.Create)
            .Register(SampleNodeTypes.OrderSink, context => OrderSinkNode.Create(context, store));
    }
}
