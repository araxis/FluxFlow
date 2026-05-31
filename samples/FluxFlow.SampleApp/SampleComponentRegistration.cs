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

        return registry.Register(new SampleOrderModule(store));
    }
}

internal sealed class SampleOrderModule : IFlowNodeModule
{
    public SampleOrderModule(InMemoryOrderStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        Registrations =
        [
            new FlowNodeRegistration(SampleNodeTypes.OrderSource, OrderSourceNode.Create),
            new FlowNodeRegistration(SampleNodeTypes.OrderReview, OrderReviewNode.Create),
            new FlowNodeRegistration(
                SampleNodeTypes.OrderSink,
                context => OrderSinkNode.Create(context, store))
        ];
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}
