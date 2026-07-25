using FluxFlow.Composition;
using FluxFlow.Nodes;

namespace FluxFlow.SampleApp;

internal sealed record OrderSourceOptions
{
    public SampleOrder[] Orders { get; init; } = [];
}

internal sealed record OrderSinkOptions
{
    public string Category { get; init; } = "default";
}

internal sealed class OrderSourceNode(IReadOnlyList<SampleOrder> orders) : FlowSource<SampleOrder>(
    new FlowSourceOptions { OutputCapacity = 8 })
{
    public static ValueTask<ComposedNode> Create(CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<OrderSourceOptions>();
        var node = new OrderSourceNode(options.Orders);
        return ValueTask.FromResult(ComposedNode.Create(
            node,
            outputs: [CompositionPorts.Output<SampleOrder>("Output", node.Output)],
            events: node.Events,
            errors: node.Errors));
    }

    protected override Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var order in orders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Emit(FlowMessage.Create(order));
        }

        return Task.CompletedTask;
    }
}

internal sealed class OrderReviewNode : FlowNode<SampleOrder, ReviewedOrder>
{
    private OrderReviewNode()
        : base(new FlowNodeOptions { InputCapacity = 8 })
    {
    }

    public static ValueTask<ComposedNode> Create(CompositionNodeFactoryContext context)
    {
        var node = new OrderReviewNode();
        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs: [CompositionPorts.Input<SampleOrder>("Input", node.Input)],
            outputs: [CompositionPorts.Output<ReviewedOrder>("Output", node.Output)],
            events: node.Events,
            errors: node.Errors));
    }

    protected override Task ProcessAsync(FlowMessage<SampleOrder> message)
    {
        var reviewed = new ReviewedOrder(
            message.Payload.Id,
            message.Payload.Customer,
            message.Payload.Total,
            Priority: message.Payload.Total >= 100m);

        Emit(message.With(reviewed));
        EmitEvent(new FlowEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            CorrelationId = message.CorrelationId,
            Name = "sample.order.reviewed",
            Message = $"Reviewed order {message.Payload.Id}.",
            Attributes = new Dictionary<string, object?>
            {
                ["orderId"] = message.Payload.Id,
                ["priority"] = reviewed.Priority
            }
        });
        return Task.CompletedTask;
    }
}

internal sealed class OrderSinkNode : FlowNode<ReviewedOrder, ReviewedOrder>
{
    private readonly string _category;
    private readonly InMemoryOrderStore _store;

    private OrderSinkNode(string category, InMemoryOrderStore store)
        : base(new FlowNodeOptions { InputCapacity = 8 })
    {
        _category = category;
        _store = store;
    }

    public static ValueTask<ComposedNode> Create(
        CompositionNodeFactoryContext context,
        InMemoryOrderStore store)
    {
        var options = context.BindConfiguration<OrderSinkOptions>();
        var node = new OrderSinkNode(options.Category, store);
        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs: [CompositionPorts.Input<ReviewedOrder>("Input", node.Input)],
            events: node.Events,
            errors: node.Errors));
    }

    protected override Task ProcessAsync(FlowMessage<ReviewedOrder> message)
    {
        _store.Add(_category, message.Payload);
        EmitEvent(new FlowEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            CorrelationId = message.CorrelationId,
            Name = "sample.order.stored",
            Message = $"Stored order {message.Payload.Id}.",
            Attributes = new Dictionary<string, object?>
            {
                ["orderId"] = message.Payload.Id,
                ["category"] = _category,
                ["customer"] = message.Payload.Customer
            }
        });
        return Task.CompletedTask;
    }
}

internal sealed class EventCollectorNode : FlowNode<CompositionComponentEvent, CompositionComponentEvent>
{
    private readonly InMemoryComponentEventCollector _collector;

    private EventCollectorNode(InMemoryComponentEventCollector collector)
        : base(new FlowNodeOptions { InputCapacity = 16 })
    {
        _collector = collector;
    }

    public static ValueTask<ComposedNode> Create(
        CompositionNodeFactoryContext context,
        InMemoryComponentEventCollector collector)
    {
        var node = new EventCollectorNode(collector);
        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs: [CompositionPorts.Input<CompositionComponentEvent>("Input", node.Input)],
            events: node.Events,
            errors: node.Errors));
    }

    protected override Task ProcessAsync(FlowMessage<CompositionComponentEvent> message)
    {
        _collector.Add(message.Payload);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryComponentEventCollector
{
    private readonly List<CompositionComponentEvent> _events = [];

    public IReadOnlyList<CompositionComponentEvent> GetSnapshot()
    {
        lock (_events)
        {
            return _events.ToArray();
        }
    }

    public void Add(CompositionComponentEvent value)
    {
        lock (_events)
        {
            _events.Add(value);
        }
    }
}
