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
    public static OrderSourceNode Create(ComponentActivationContext context)
    {
        var options = context.BindConfiguration<OrderSourceOptions>();
        return new OrderSourceNode(options.Orders);
    }

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var order in orders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EmitAsync(FlowMessage.Create(order), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}

internal sealed class OrderReviewNode : FlowNode<SampleOrder, ReviewedOrder>
{
    private OrderReviewNode()
        : base(new FlowNodeOptions { InputCapacity = 8 })
    {
    }

    public static OrderReviewNode Create(ComponentActivationContext context)
        => new();

    protected override async Task ProcessAsync(FlowMessage<SampleOrder> message)
    {
        var reviewed = new ReviewedOrder(
            message.Value.Id,
            message.Value.Customer,
            message.Value.Total,
            Priority: message.Value.Total >= 100m);

        await EmitAsync(message.With(reviewed), Stopping).ConfigureAwait(false);
        EmitEvent(new FlowEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            CorrelationId = message.CorrelationId,
            Name = "sample.order.reviewed",
            Message = $"Reviewed order {message.Value.Id}.",
            Attributes = new Dictionary<string, object?>
            {
                ["orderId"] = message.Value.Id,
                ["priority"] = reviewed.Priority
            }
        });
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

    public static OrderSinkNode Create(
        ComponentActivationContext context,
        InMemoryOrderStore store)
    {
        var options = context.BindConfiguration<OrderSinkOptions>();
        return new OrderSinkNode(options.Category, store);
    }

    protected override Task ProcessAsync(FlowMessage<ReviewedOrder> message)
    {
        _store.Add(_category, message.Value);
        EmitEvent(new FlowEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            CorrelationId = message.CorrelationId,
            Name = "sample.order.stored",
            Message = $"Stored order {message.Value.Id}.",
            Attributes = new Dictionary<string, object?>
            {
                ["orderId"] = message.Value.Id,
                ["category"] = _category,
                ["customer"] = message.Value.Customer
            }
        });
        return Task.CompletedTask;
    }
}

internal sealed class EventCollectorNode : FlowNode<ComponentEvent, ComponentEvent>
{
    private readonly InMemoryComponentEventCollector _collector;

    private EventCollectorNode(InMemoryComponentEventCollector collector)
        : base(new FlowNodeOptions { InputCapacity = 16 })
    {
        _collector = collector;
    }

    public static EventCollectorNode Create(
        ComponentActivationContext context,
        InMemoryComponentEventCollector collector)
        => new(collector);

    protected override Task ProcessAsync(FlowMessage<ComponentEvent> message)
    {
        _collector.Add(message.Value);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryComponentEventCollector
{
    private readonly List<ComponentEvent> _events = [];

    public IReadOnlyList<ComponentEvent> GetSnapshot()
    {
        lock (_events)
        {
            return _events.ToArray();
        }
    }

    public void Add(ComponentEvent value)
    {
        lock (_events)
        {
            _events.Add(value);
        }
    }
}
