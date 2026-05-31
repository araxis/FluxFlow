using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.SampleApp;

internal sealed class OrderSourceNode(IReadOnlyList<SampleOrder> orders) : SourceFlowNode<SampleOrder>(
    new DataflowBlockOptions { BoundedCapacity = 8 })
{
    public static RuntimeNode Create(RuntimeNodeFactoryContext context)
    {
        var node = new OrderSourceNode(ReadRequired<List<SampleOrder>>(context.Definition, "orders"));
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

internal sealed class OrderReviewNode : MapFlowNode<SampleOrder, ReviewedOrder>
{
    private OrderReviewNode()
        : base(
            new ExecutionDataflowBlockOptions { BoundedCapacity = 8 },
            new DataflowBlockOptions { BoundedCapacity = 8 })
    {
    }

    public static RuntimeNode Create(RuntimeNodeFactoryContext context)
    {
        var node = new OrderReviewNode();
        return context.CreateNode(node)
            .Input("Input", node.Input)
            .Output("Output", node.Output)
            .Build();
    }

    protected override ValueTask<ReviewedOrder> MapAsync(
        SampleOrder input,
        CancellationToken cancellationToken)
    {
        var reviewed = new ReviewedOrder(
            input.Id,
            input.Customer,
            input.Total,
            Priority: input.Total >= 100m);

        TryEmitDiagnostic(
            "sample.order.reviewed",
            message: $"Reviewed order {input.Id}.",
            attributes: new Dictionary<string, object?>
            {
                ["orderId"] = input.Id,
                ["priority"] = reviewed.Priority
            });

        return ValueTask.FromResult(reviewed);
    }
}

internal sealed class OrderSinkNode : EventFlowNodeBase
{
    private readonly string _category;
    private readonly InMemoryOrderStore _store;
    private readonly ActionBlock<ReviewedOrder> _input;

    private OrderSinkNode(string category, InMemoryOrderStore store)
    {
        _category = category;
        _store = store;
        _input = new ActionBlock<ReviewedOrder>(
            HandleAsync,
            new ExecutionDataflowBlockOptions { BoundedCapacity = 8 });
        CompleteWhen(_input.Completion);
    }

    public ITargetBlock<ReviewedOrder> Input => _input;

    public static RuntimeNode Create(RuntimeNodeFactoryContext context, InMemoryOrderStore store)
    {
        var node = new OrderSinkNode(ReadOptional(context.Definition, "category", "default"), store);
        return context.CreateNode(node)
            .Input("Input", node.Input)
            .Build();
    }

    public override void Complete()
        => _input.Complete();

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ((IDataflowBlock)_input).Fault(exception);
        FaultNode(exception);
    }

    private Task HandleAsync(ReviewedOrder order)
    {
        _store.Add(_category, order);
        EmitEvent(
            "sample.order.stored",
            subject: order.Id,
            status: _category,
            channel: "sample.orders",
            attributes: new Dictionary<string, string>
            {
                ["customer"] = order.Customer
            });
        TryEmitDiagnostic(
            "sample.order.stored",
            message: $"Stored order {order.Id}.",
            attributes: new Dictionary<string, object?>
            {
                ["orderId"] = order.Id,
                ["category"] = _category
            });

        return Task.CompletedTask;
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
