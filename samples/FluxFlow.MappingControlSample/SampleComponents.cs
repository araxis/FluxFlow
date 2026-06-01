using FluxFlow.Components.Control.Contracts;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.MappingControlSample;

internal static class SampleNodeTypes
{
    public static readonly NodeType OrderSource = new("sample.orders");
    public static readonly NodeType OrderSink = new("sample.order-sink");
    public static readonly NodeType AssertionSink = new("sample.assertion-sink");
}

internal static class SampleComponentRegistration
{
    public static RuntimeNodeFactoryRegistry RegisterSampleComponents(
        this RuntimeNodeFactoryRegistry registry,
        SampleStore store)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(store);

        return registry.Register(new SampleComponentModule(store));
    }
}

internal sealed class SampleComponentModule : IFlowNodeModule
{
    public SampleComponentModule(SampleStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        Registrations =
        [
            new FlowNodeRegistration(SampleNodeTypes.OrderSource, OrderSourceNode.Create),
            new FlowNodeRegistration(
                SampleNodeTypes.OrderSink,
                context => OrderSinkNode.Create(context, store)),
            new FlowNodeRegistration(
                SampleNodeTypes.AssertionSink,
                context => AssertionSinkNode.Create(context, store))
        ];
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}

internal sealed class OrderSourceNode(IReadOnlyList<IncomingOrder> orders) : SourceFlowNode<IncomingOrder>(
    new DataflowBlockOptions { BoundedCapacity = 8 })
{
    public static RuntimeNode Create(RuntimeNodeFactoryContext context)
    {
        var node = new OrderSourceNode(ReadRequired<List<IncomingOrder>>(context.Definition, "orders"));
        return context.CreateNode(node)
            .Output("Output", node.Output)
            .Build();
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        foreach (var order in orders)
        {
            await SendOutputAsync(order, cancellationToken).ConfigureAwait(false);
        }

        CompleteOutput();
    }

    private static T ReadRequired<T>(NodeDefinition definition, string name)
    {
        if (!definition.Configuration.TryGetValue(name, out var value))
        {
            throw new InvalidOperationException($"Missing required option '{name}'.");
        }

        return value.Deserialize<T>() ?? throw new InvalidOperationException($"Option '{name}' was empty.");
    }
}

internal sealed class OrderSinkNode : SinkFlowNode<ReviewedOrder>
{
    private readonly string _category;
    private readonly SampleStore _store;

    private OrderSinkNode(string category, SampleStore store)
        : base(new ExecutionDataflowBlockOptions { BoundedCapacity = 8 })
    {
        _category = category;
        _store = store;
    }

    public static RuntimeNode Create(RuntimeNodeFactoryContext context, SampleStore store)
    {
        var node = new OrderSinkNode(ReadOptional(context.Definition, "category", "default"), store);
        return context.CreateNode(node)
            .Input("Input", node.Input)
            .Build();
    }

    protected override ValueTask HandleAsync(
        ReviewedOrder input,
        CancellationToken cancellationToken)
    {
        _store.AddOrder(_category, input);
        return ValueTask.CompletedTask;
    }

    private static string ReadOptional(NodeDefinition definition, string name, string fallback)
    {
        if (!definition.Configuration.TryGetValue(name, out var value))
        {
            return fallback;
        }

        return value.GetString() ?? fallback;
    }
}

internal sealed class AssertionSinkNode(SampleStore store) : SinkFlowNode<ControlAssertionResult>(
    new ExecutionDataflowBlockOptions { BoundedCapacity = 8 })
{
    public static RuntimeNode Create(RuntimeNodeFactoryContext context, SampleStore store)
    {
        var node = new AssertionSinkNode(store);
        return context.CreateNode(node)
            .Input("Input", node.Input)
            .Build();
    }

    protected override ValueTask HandleAsync(
        ControlAssertionResult input,
        CancellationToken cancellationToken)
    {
        store.AddAssertion(new SampleAssertion(input.Name, input.Passed, input.Message));
        return ValueTask.CompletedTask;
    }
}
