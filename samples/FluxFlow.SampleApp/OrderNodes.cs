using FluxFlow.Composition;
using FluxFlow.Data;
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

internal sealed class OrderSourceNode(IReadOnlyList<SampleOrder> orders) : FlowSource(
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
            await EmitAsync(FlowMessage.Create(FlowValue.From(order)), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}

internal sealed class OrderReviewNode : FlowNode
{
    private OrderReviewNode()
        : base(new FlowNodeOptions { InputCapacity = 8 })
    {
    }

    public static OrderReviewNode Create(ComponentActivationContext context)
        => new();

    protected override async Task ProcessAsync(FlowMessage message)
    {
        var order = message.Value!.Deserialize<SampleOrder>()
            ?? throw new InvalidOperationException("A sample order is required.");
        var reviewed = new ReviewedOrder(
            order.Id,
            order.Customer,
            order.Total,
            Priority: order.Total >= 100m);

        await EmitAsync(message.With(FlowValue.From(reviewed)), Stopping).ConfigureAwait(false);
        EmitEvent(new FlowEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            CorrelationId = message.CorrelationId,
            Name = "sample.order.reviewed",
            Message = $"Reviewed order {order.Id}.",
            Attributes = new Dictionary<string, object?>
            {
                ["orderId"] = order.Id,
                ["priority"] = reviewed.Priority
            }
        });
    }
}

internal sealed class OrderSinkNode : FlowNode
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

    protected override Task ProcessAsync(FlowMessage message)
    {
        var reviewed = message.Value!.Deserialize<ReviewedOrder>()
            ?? throw new InvalidOperationException("A reviewed order is required.");
        _store.Add(_category, reviewed);
        EmitEvent(new FlowEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            CorrelationId = message.CorrelationId,
            Name = "sample.order.stored",
            Message = $"Stored order {reviewed.Id}.",
            Attributes = new Dictionary<string, object?>
            {
                ["orderId"] = reviewed.Id,
                ["category"] = _category,
                ["customer"] = reviewed.Customer
            }
        });
        return Task.CompletedTask;
    }
}

internal sealed class EventCollectorNode : FlowNode
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

    protected override Task ProcessAsync(FlowMessage message)
    {
        _collector.Add(message.Value ?? global::FluxFlow.Data.FlowValue.Null);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryComponentEventCollector
{
    private readonly List<global::FluxFlow.Data.FlowValue> _events = [];

    public IReadOnlyList<global::FluxFlow.Data.FlowValue> GetSnapshot()
    {
        lock (_events)
        {
            return _events.ToArray();
        }
    }

    public void Add(global::FluxFlow.Data.FlowValue value)
    {
        lock (_events)
        {
            _events.Add(value);
        }
    }
}
